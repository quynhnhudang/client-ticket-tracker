# Deploying to AWS EC2 (Ubuntu + nginx + Kestrel + SQL Server)

You run every command yourself. Each step ends with **Check:** what you should
see before moving on. If a check fails, stop there: later steps build on it.

```
            Internet
               │  :80 / :443 (only ports open, plus :22 from your IP)
        ┌──────▼───────────────────────── EC2 (Ubuntu 24.04) ─────────────────┐
        │  nginx                                                              │
        │   ├─ /            → /var/www/tickettracker/current/web  (Angular)   │
        │   └─ /api/*       → http://127.0.0.1:5080  (Kestrel, systemd)       │
        │                                   │                                 │
        │                     SQL Server 2025 Express on 127.0.0.1:1433       │
        └─────────────────────────────────────────────────────────────────────┘
```

**Why everything on one instance?** It's the cheapest setup that still shows every
piece: a reverse proxy, a supervised app process, a real SQL Server. The trade-off:
SQL Server needs at least 2 GB of RAM on its own, so the instance must be a
**t3.medium (4 GB)** or larger; a free-tier micro instance won't run it.
The alternative is **Amazon RDS for SQL Server** (managed backups and patching, a
separate bill). With RDS you skip step 4, and only the connection strings change.
Check current prices on the AWS pricing pages, and stop the instance when you
aren't demoing. Its disk, and any public IPv4 address, still cost a little while stopped.

Throughout: `ubuntu` is the login user; `tickettracker` is a no-login system
user that runs the app. Replace `YOUR_IP` with the instance's public IP.

---

## 1. Launch the instance (AWS console)

1. **EC2 → Launch instance**
   - Name: `ticket-tracker`
   - AMI: **Ubuntu Server 24.04 LTS** (64-bit x86). SQL Server doesn't run on ARM/Graviton.
   - Type: **t3.medium**
   - Key pair: create one, and download the `.pem` file
   - Storage: **30 GiB gp3**
2. **Security group** (the instance's firewall). Create one named `ticket-tracker-sg`:
   | Type | Port | Source | Why |
   |---|---|---|---|
   | SSH | 22 | **My IP** | only you can log in |
   | HTTP | 80 | 0.0.0.0/0 | the site |
   | HTTPS | 443 | 0.0.0.0/0 | the site, once step 10 is done |

   Don't open 1433 (SQL Server) or 5080 (Kestrel). They stay private to the machine.
3. Recommended: **Elastic IPs → Allocate → Associate** with the instance, so the IP
   doesn't change when you stop and start it.

On your laptop:
```bash
chmod 400 ~/Downloads/ticket-tracker.pem
ssh -i ~/Downloads/ticket-tracker.pem ubuntu@YOUR_IP
```
**Check:** you get an `ubuntu@ip-...:~$` prompt.

---

## 2. Base system setup

```bash
sudo apt-get update && sudo apt-get -y upgrade
sudo timedatectl set-timezone UTC          # logs and the app both use UTC

# 2 GB swap: headroom for the Angular build while SQL Server holds most of the RAM
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

# Host firewall: a second layer behind the security group
sudo ufw allow OpenSSH
sudo ufw allow 'Nginx Full' 2>/dev/null || { sudo ufw allow 80/tcp; sudo ufw allow 443/tcp; }
sudo ufw --force enable
```
If `upgrade` mentions a new kernel, run `sudo reboot`, wait a minute, and ssh back in.

**Check:** `free -h` shows `Swap: 2.0Gi`; `sudo ufw status` lists OpenSSH and 80/443.

---

## 3. Install .NET 10, Node.js 24, nginx and git

```bash
# .NET SDK 10 from Ubuntu's own archive (includes the ASP.NET Core runtime).
# The SDK is needed because deploy.sh builds on the server.
sudo apt-get install -y dotnet-sdk-10.0 nginx git curl

# Stop apt from ever mixing in .NET packages from Microsoft's repository (added
# in step 4 for SQL Server tools). Two sources for the same packages is a
# known cause of broken .NET installs.
sudo tee /etc/apt/preferences.d/dotnet-from-ubuntu-only > /dev/null <<'EOF'
Package: dotnet* aspnetcore* netstandard*
Pin: origin "packages.microsoft.com"
Pin-Priority: -10
EOF

# Node.js 24 LTS from NodeSource (Ubuntu's own nodejs package is too old for Angular 22)
curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
sudo apt-get install -y nodejs

# EF Core command-line tool, used by deploy.sh to build the migrations bundle
dotnet tool install --global dotnet-ef
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc && source ~/.bashrc
```
**Check:**
```bash
dotnet --list-runtimes     # Microsoft.AspNetCore.App 10.0.x
node --version             # v24.x
dotnet ef --version        # 10.x
nginx -v
```
Also open `http://YOUR_IP` in a browser: you should see nginx's "Welcome" page.

---

## 4. Install SQL Server 2025 (skip if you use RDS)

```bash
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | sudo gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/mssql-server-2025.list | sudo tee /etc/apt/sources.list.d/mssql-server-2025.list
sudo apt-get update
sudo apt-get install -y mssql-server
sudo /opt/mssql/bin/mssql-conf setup
```
In `mssql-conf setup`: choose **Express**, accept the license, and set a strong **sa** password.
Store it in a password manager; you'll need it in steps 5 and 9.

> **Why Express, not Developer?** Developer edition is licensed for development and
> testing only. A publicly reachable site counts as production use. Express is free
> for production, and its limits (10 GB per database, capped memory) are far above
> what this app needs.

```bash
# Cap SQL Server's memory so it leaves room for the app, nginx and builds.
# By default it would try to use most of the RAM.
sudo /opt/mssql/bin/mssql-conf set memory.memorylimitmb 2048
# Listen on loopback only: SQL Server is unreachable from outside the machine,
# even if a security-group rule is ever wrong.
sudo /opt/mssql/bin/mssql-conf set network.ipaddress 127.0.0.1
# Full-text search (the app falls back to LIKE queries without it)
sudo apt-get install -y mssql-server-fts
sudo systemctl restart mssql-server

# sqlcmd, from Microsoft's "prod" repository
curl -sSL -O https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt-get update
sudo ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> ~/.bashrc && source ~/.bashrc
```
**Check:**
```bash
systemctl is-active mssql-server                  # active
sudo ss -ltnp | grep 1433                         # 127.0.0.1:1433 only, NOT 0.0.0.0:1433
sqlcmd -S localhost -U sa -C -Q "SELECT @@VERSION; SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS FTS;"
```
The last command should show SQL Server 2025, Express Edition, and `FTS = 1`.
(`-C` trusts the server's self-signed certificate; that's fine here because the connection never leaves the machine.)
Also confirm `dotnet --list-runtimes` still works: the pin from step 3 kept .NET intact.

---

## 5. Create the database and least-privilege logins

```bash
cd ~ && git clone https://github.com/YOUR_USER/client-ticket-tracker.git   # step 6 moves it into place
read -rs -p "App login password: " APP_PW; echo
read -rs -p "Migrator login password: " MIG_PW; echo
sqlcmd -S localhost -U sa -C -i client-ticket-tracker/deploy/sql/create-logins.sql \
       -v AppPassword="$APP_PW" MigratorPassword="$MIG_PW"
```
(`read -s` keeps the passwords out of your shell history. For a private repo, clone
with a GitHub personal access token or a read-only deploy key.)

**Check:** the final result lists `tickettracker_app` with `db_datareader` and
`db_datawriter`, and `tickettracker_migrator` with `db_owner`.

Keep `$APP_PW` and `$MIG_PW` in this shell session for step 6.

---

## 6. Put the code and configuration in place

```bash
# Code: owned by ubuntu, who runs deploy.sh
sudo mkdir -p /opt/tickettracker && sudo chown ubuntu:ubuntu /opt/tickettracker
mv ~/client-ticket-tracker /opt/tickettracker/src

# The service account: no password, no shell, no home directory
sudo useradd --system --no-create-home --shell /usr/sbin/nologin tickettracker

# Releases (written by ubuntu, read by the service) and the Data Protection key store
sudo mkdir -p /var/www/tickettracker/releases && sudo chown -R ubuntu:ubuntu /var/www/tickettracker
sudo mkdir -p /var/lib/tickettracker/keys && sudo chown -R tickettracker:tickettracker /var/lib/tickettracker
sudo chmod 700 /var/lib/tickettracker/keys

# Secrets
sudo mkdir -p /etc/tickettracker
sudo cp /opt/tickettracker/src/deploy/env/app.env.example    /etc/tickettracker/app.env
sudo cp /opt/tickettracker/src/deploy/env/deploy.env.example /etc/tickettracker/deploy.env
sudo chown root:tickettracker /etc/tickettracker/app.env    && sudo chmod 640 /etc/tickettracker/app.env
sudo chown root:ubuntu        /etc/tickettracker/deploy.env && sudo chmod 640 /etc/tickettracker/deploy.env
sudo nano /etc/tickettracker/app.env      # fill in every CHANGE_ME (the app password is $APP_PW)
sudo nano /etc/tickettracker/deploy.env   # the migrator password is $MIG_PW
```
In `app.env`, set `Email__AppBaseUrl=http://YOUR_IP` (it becomes the "View ticket" link in emails).
To start without email, leave `Email__Host=` empty.

**Why these permissions?** `app.env` is readable by root and the service only;
`deploy.env` by root and the deploy user only. The app never sees the migrator password.

**Check:**
```bash
ls -l /etc/tickettracker/                                  # -rw-r----- root tickettracker app.env / root ubuntu deploy.env
sudo -u tickettracker head -1 /etc/tickettracker/app.env   # readable by the service
grep -c CHANGE_ME /etc/tickettracker/*.env                 # 0 for both (unless email is intentionally off)
```

---

## 7. Install the systemd unit and the nginx site (one-time)

```bash
cd /opt/tickettracker/src
sudo cp deploy/systemd/tickettracker.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable tickettracker        # start at boot; deploy.sh does the first start

sudo cp deploy/nginx/tickettracker.conf /etc/nginx/sites-available/tickettracker
sudo ln -sf /etc/nginx/sites-available/tickettracker /etc/nginx/sites-enabled/tickettracker
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
```
**Check:** `nginx -t` prints `syntax is ok` and `test is successful`, and
`systemctl is-enabled tickettracker` prints `enabled`. (The site shows a 404 until
the first deploy creates `/var/www/tickettracker/current`.)

---

## 8. First deploy

```bash
cd /opt/tickettracker/src
./deploy/deploy.sh
```
It runs tests, publishes the API, builds Angular, applies migrations (creating
the tables), switches `current`, restarts the service, waits for `/api/health`,
and reloads nginx. The first run takes a few minutes (NuGet and npm downloads).

**Check:**
```bash
systemctl status tickettracker --no-pager     # active (running)
curl -s http://127.0.0.1:5080/api/health      # {"status":"ok","database":"reachable"}
journalctl -u tickettracker -n 20 --no-pager  # includes "Created initial agent ..."
```
From your laptop: `curl http://YOUR_IP/api/health` gives the same JSON. Then open
`http://YOUR_IP`, sign in with the `SeedAgent` email and password, and create a ticket.

---

## 9. Finish the database setup

```bash
cd /opt/tickettracker/src
sqlcmd -S localhost -U sa -C -i db/fulltext.sql        # full-text index + SearchText column
sqlcmd -S localhost -U sa -C -i db/sample-data.sql     # optional: demo tickets
sudo sed -i '/^SeedAgent__Password=/d' /etc/tickettracker/app.env   # the seed password has done its job
sudo systemctl restart tickettracker
```
**Check:** `fulltext.sql` ends with `1 1`. In the app, search for `login timeout`:
the results line says **full-text search**. Escalate a ticket: the email arrives
(check Mailtrap), or `journalctl -u tickettracker` shows the escalation log line if email is off.

---

## 10. HTTPS (needs a domain name)

Let's Encrypt only issues certificates for domain names, not raw IPs.
1. At your DNS provider, create an **A record** for, say, `tickets.yourdomain.com` pointing at the Elastic IP.
   Check: `dig +short tickets.yourdomain.com` prints the IP.
2. In `/etc/nginx/sites-available/tickettracker`, change `server_name _;` to `server_name tickets.yourdomain.com;`, then `sudo nginx -t && sudo systemctl reload nginx`.
3. ```bash
   sudo apt-get install -y certbot python3-certbot-nginx
   sudo certbot --nginx -d tickets.yourdomain.com --redirect -m you@example.com --agree-tos
   ```
   certbot adds the `listen 443 ssl` block, the certificate paths, an HTTP→HTTPS
   redirect, and a timer that renews automatically. (That's why deploy.sh never
   overwrites this file.)
4. Set `Email__AppBaseUrl=https://tickets.yourdomain.com` in `app.env`, then `sudo systemctl restart tickettracker`.

**Check:** `https://tickets.yourdomain.com` shows the padlock; `http://` redirects to it;
`sudo certbot renew --dry-run` succeeds. In the browser dev tools (Application → Cookies),
`ctt.auth` now has **Secure** ticked. nginx sends `X-Forwarded-Proto: https`, and
`UseForwardedHeaders` makes the app mark the cookie Secure.

**Without a domain** the site runs on plain HTTP, so the password and cookie cross
the network unencrypted. That's acceptable for a short demo, but say so if asked.

---

## Day-to-day operations

| Task | Command |
|---|---|
| Deploy the latest `main` | `cd /opt/tickettracker/src && ./deploy/deploy.sh` |
| Deploy faster (tests already ran in CI or on your laptop) | `./deploy/deploy.sh --skip-tests` |
| Roll back to the previous release | `./deploy/deploy.sh --rollback` (code only; migrations stay applied) |
| Follow the app log | `journalctl -u tickettracker -f` |
| nginx logs | `sudo tail -f /var/log/nginx/access.log /var/log/nginx/error.log` |
| Restart the app only | `sudo systemctl restart tickettracker` |
| Which release is live? | `readlink /var/www/tickettracker/current` |
| OS security updates | `sudo apt-get update && sudo apt-get -y upgrade` (then restart services or reboot) |
| Database backup | `sqlcmd -S localhost -U sa -C -Q "BACKUP DATABASE TicketTracker TO DISK='/var/opt/mssql/data/tt.bak' WITH INIT"`, then copy it off the machine (e.g. to S3) |

## Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| **502 Bad Gateway** | Kestrel isn't running. `systemctl status tickettracker`, then `journalctl -u tickettracker -n 50`. Common: a typo in `app.env`, or `ValidateOnStart` rejecting incomplete `Email__*` settings. |
| Health says `"database":"unreachable"` (503) | SQL Server is down (`systemctl status mssql-server`), or the app password in `app.env` is wrong. Test it: `sqlcmd -S localhost -U tickettracker_app -C -Q "SELECT 1"`. |
| **404 when refreshing** `/tickets/5` | The nginx `try_files ... /index.html` line is missing, or the default site is still enabled. |
| Everyone is logged out after every restart | The Data Protection key directory isn't writable: `ls -ld /var/lib/tickettracker/keys` should show owner `tickettracker`. |
| Login always returns **429** | 5 attempts per minute per IP. If every client IP looks like `127.0.0.1` in the logs, nginx isn't sending `X-Forwarded-For`. |
| `npm ci` / `ng build` gets "Killed" | Out of memory. Check that swap is on (`free -h`), or build with SQL Server stopped. |
| deploy.sh: "dotnet-ef is not installed" | Run step 3's `dotnet tool install` as the **ubuntu** user. |
| Search says "LIKE fallback" | `mssql-server-fts` isn't installed or `db/fulltext.sql` wasn't run. Do step 9, then restart the service. |
| Email not arriving | `journalctl -u tickettracker | grep -i escalat`. Look for SMTP authentication errors, and check the `Email__*` values (port 2525 or 587 with `StartTls`). |
