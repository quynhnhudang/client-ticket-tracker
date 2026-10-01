#!/usr/bin/env bash
#
# deploy.sh: build, migrate, switch, restart, health-check and reload.
# Run on the EC2 instance, from anywhere, as the "ubuntu" user (it uses sudo
# for systemctl and nginx only).
#
#   ./deploy/deploy.sh                # pull, test, build, migrate, release
#   ./deploy/deploy.sh --skip-tests   # same, without dotnet test / ng test
#   ./deploy/deploy.sh --no-pull      # deploy the working copy as it is
#   ./deploy/deploy.sh --rollback     # switch back to the previous release
#
# How a release works:
#   Each deploy builds into a NEW folder /var/www/tickettracker/releases/<timestamp>,
#   then repoints the "current" symlink at it in one atomic step. nginx and
#   systemd only ever reference "current". So:
#     - a failed build never touches the running site,
#     - rollback is just pointing the symlink back at the previous folder,
#     - the last few releases stay on disk for exactly that.

set -Eeuo pipefail   # stop on any error, unset variable, or failure inside a pipe

# ------------------------------------------------------------------ settings
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ROOT=/var/www/tickettracker
RELEASES="$APP_ROOT/releases"
CURRENT="$APP_ROOT/current"
SERVICE=tickettracker
HEALTH_URL=http://127.0.0.1:5080/api/health
DEPLOY_ENV=/etc/tickettracker/deploy.env
KEEP_RELEASES=5

SKIP_TESTS=false
PULL=true
ROLLBACK=false
for arg in "$@"; do
  case "$arg" in
    --skip-tests) SKIP_TESTS=true ;;
    --no-pull)    PULL=false ;;
    --rollback)   ROLLBACK=true ;;
    -h|--help)    sed -n '2,20p' "$0"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

log()  { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
fail() { printf '\n\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }
trap 'fail "line $LINENO: \"$BASH_COMMAND\" failed"' ERR

# ------------------------------------------------------------------ helpers

# Point "current" at a release atomically. ln -sfn alone deletes and recreates
# the link, leaving a moment with no "current". Building a temporary link and
# renaming it over the old one (mv -T) is a single rename() system call, so
# every request sees either the old release or the new one, never nothing.
switch_to() {
  ln -sfn "$1" "$APP_ROOT/current.tmp"
  mv -Tf "$APP_ROOT/current.tmp" "$CURRENT"
}

# Poll /api/health until it answers 200 (app up AND database reachable), up to 30s.
wait_for_health() {
  for _ in $(seq 1 30); do
    if curl -fsS --max-time 2 "$HEALTH_URL" > /dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

restart_and_check() {
  sudo systemctl restart "$SERVICE"
  wait_for_health
}

# The release just OLDER than the one "current" points at (not merely the newest
# other one: after a rollback, the newest release is the one we rolled away from).
previous_release() {
  local current_target
  current_target="$(readlink -e "$CURRENT" || true)"
  # Releases are named by UTC timestamp, so a name sort is a date sort.
  find "$RELEASES" -mindepth 1 -maxdepth 1 -type d | sort \
    | awk -v cur="$current_target" '$0 == cur { print prev; exit } { prev = $0 }'
}

# ------------------------------------------------------------------ rollback
if $ROLLBACK; then
  prev="$(previous_release)"
  [[ -n "$prev" ]] || fail "No previous release to roll back to."
  log "Rolling back to $(basename "$prev")"
  switch_to "$prev"
  restart_and_check || fail "Rolled back, but the health check still fails. See: journalctl -u $SERVICE -n 100"
  log "Rollback complete. Note: database migrations are NOT rolled back."
  exit 0
fi

# ------------------------------------------------------------------ preflight
log "Preflight checks"
for cmd in git dotnet node npm curl; do
  command -v "$cmd" > /dev/null || fail "'$cmd' is not installed (see DEPLOY.md step 3)."
done
[[ -r "$DEPLOY_ENV" ]] || fail "$DEPLOY_ENV is missing or unreadable (see DEPLOY.md step 6)."
# shellcheck source=/dev/null
source "$DEPLOY_ENV"
[[ -n "${MIGRATOR_CONNECTION:-}" ]] || fail "MIGRATOR_CONNECTION is not set in $DEPLOY_ENV."
export PATH="$PATH:$HOME/.dotnet/tools"   # where `dotnet tool install --global` puts dotnet-ef
command -v dotnet-ef > /dev/null || fail "dotnet-ef is not installed: dotnet tool install --global dotnet-ef"

cd "$REPO_DIR"

if $PULL; then
  log "Pulling latest code"
  git pull --ff-only   # refuse to merge: the server should never have local commits
fi
COMMIT="$(git rev-parse --short HEAD)"

RELEASE="$RELEASES/$(date -u +%Y%m%d%H%M%S)-$COMMIT"
BUILD="$(mktemp -d)"
trap 'rm -rf "$BUILD"' EXIT
mkdir -p "$RELEASE"
log "Building release $(basename "$RELEASE")"

# ------------------------------------------------------------------ test
if ! $SKIP_TESTS; then
  log "Running API tests"
  dotnet test api/ClientTicketTracker.slnx --configuration Release --nologo --verbosity quiet
fi

# ------------------------------------------------------------------ build API
log "Publishing API"
# Framework-dependent publish: the output contains our DLLs only, and uses the
# ASP.NET Core runtime installed on the server (apt package), which gets
# security patches through normal apt upgrades.
dotnet publish api/src/TicketTracker.Api/TicketTracker.Api.csproj \
  --configuration Release --output "$RELEASE/api" --nologo --verbosity quiet

# ------------------------------------------------------------------ build web
log "Building Angular app"
pushd web > /dev/null
npm ci --no-audit --no-fund        # exact versions from package-lock.json, clean install
if ! $SKIP_TESTS; then
  npx ng test --watch=false
fi
npx ng build --configuration production
popd > /dev/null
cp -r web/dist/web/browser "$RELEASE/web"

# ------------------------------------------------------------------ migrate
log "Applying database migrations"
# A migrations bundle is a small executable that applies any pending
# migrations. It's built from the same commit as the app, so schema and code
# can't drift apart, and it runs with the migrator login from deploy.env.
dotnet ef migrations bundle \
  --project api/src/TicketTracker.Api/TicketTracker.Api.csproj \
  --configuration Release --output "$BUILD/efbundle" --force
"$BUILD/efbundle" --connection "$MIGRATOR_CONNECTION"
# Migrations run BEFORE the switch, so for a few seconds the OLD code runs
# against the NEW schema. Migrations must therefore be backward-compatible
# (add columns/tables first; remove old ones in a later deploy).

# ------------------------------------------------------------------ switch + restart
PREVIOUS="$(readlink -e "$CURRENT" || true)"   # -e: empty on the very first deploy
log "Switching 'current' to the new release and restarting $SERVICE"
switch_to "$RELEASE"

if ! restart_and_check; then
  echo "Health check failed. Last log lines:" >&2
  sudo journalctl -u "$SERVICE" -n 30 --no-pager >&2 || true
  if [[ -n "$PREVIOUS" && -d "$PREVIOUS" ]]; then
    log "Rolling back to $(basename "$PREVIOUS")"
    switch_to "$PREVIOUS"
    restart_and_check || true
  fi
  rm -rf "$RELEASE"   # so --rollback can never pick the broken release
  fail "Deploy failed; the previous release is running again."
fi

# ------------------------------------------------------------------ reload nginx
log "Testing and reloading nginx"
# nginx -t validates the config first; reload (not restart) swaps in new
# worker processes without dropping open connections.
sudo nginx -t
sudo systemctl reload nginx

# Also check through nginx, i.e. the path a real user takes.
curl -fsS --max-time 5 http://127.0.0.1/api/health > /dev/null || fail "App is healthy, but not reachable through nginx."

# ------------------------------------------------------------------ prune
log "Keeping the newest $KEEP_RELEASES releases"
find "$RELEASES" -mindepth 1 -maxdepth 1 -type d | sort | head -n -"$KEEP_RELEASES" | xargs -r rm -rf

log "Deployed $COMMIT: $(basename "$RELEASE")"
