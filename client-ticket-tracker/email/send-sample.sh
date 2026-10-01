#!/usr/bin/env bash
# Sends email/escalation-sample.eml through any SMTP server, so you can check how
# it renders in real inboxes (Gmail, Outlook, Apple Mail) without running the app.
#
# Usage (Mailtrap sandbox: copy host/port/user/pass from your inbox's SMTP settings):
#   SMTP_URL=smtp://sandbox.smtp.mailtrap.io:2525 SMTP_USER=xxxx SMTP_PASS=yyyy \
#   FROM=tickets@example.com TO=you@example.com ./email/send-sample.sh
#
# To reach a REAL inbox (e.g. your Gmail), use a provider that delivers mail:
# Mailtrap's sending service, a SendGrid trial account, etc. FROM must then be an
# address or domain you've verified with that provider, or it will be rejected
# or land in spam.
set -euo pipefail

: "${SMTP_URL:?e.g. smtp://sandbox.smtp.mailtrap.io:2525}"
: "${SMTP_USER:?}"
: "${SMTP_PASS:?}"
: "${FROM:?sender address}"
: "${TO:?recipient address}"

here="$(cd "$(dirname "$0")" && pwd)"
message="$(mktemp)"
trap 'rm -f "$message"' EXIT

# Replace the placeholder From/To headers in the sample with the real addresses.
sed -e "s/^From: .*/From: Client Ticket Tracker <${FROM}>/" \
    -e "s/^To: .*/To: <${TO}>/" \
    "$here/escalation-sample.eml" > "$message"

# --ssl-reqd: upgrade to TLS with STARTTLS and refuse to continue without it,
# so the password is never sent in plain text.
curl --silent --show-error --ssl-reqd \
  --url "$SMTP_URL" \
  --user "$SMTP_USER:$SMTP_PASS" \
  --mail-from "$FROM" \
  --mail-rcpt "$TO" \
  --upload-file "$message"

echo "Sent to $TO via $SMTP_URL"
