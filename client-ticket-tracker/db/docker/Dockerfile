# SQL Server 2022 Developer Edition WITH Full-Text Search, for local development.
#
# Why not just use mcr.microsoft.com/mssql/server? That official image doesn't
# include the Full-Text Search component (package mssql-server-fts). So this
# follows Microsoft's documented approach for a full-text container: start from
# plain Ubuntu and install mssql-server + mssql-server-fts from Microsoft's apt
# repository, which keeps the two package versions in step.
#
# SQL Server on Linux is x86-64 only. On an Apple Silicon Mac, docker-compose.yml
# asks for linux/amd64 and Docker Desktop emulates it (enable "Use Rosetta for
# x86_64/amd64 emulation" in Docker Desktop settings).
FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive

# Microsoft's package-signing key is installed in both places apt might look for
# it, because the repo's .list file has used both styles (signed-by and global) over time.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
 && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
      | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
 && cp /usr/share/keyrings/microsoft-prod.gpg /etc/apt/trusted.gpg.d/microsoft-prod.gpg \
 && curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list \
      -o /etc/apt/sources.list.d/mssql-server-2022.list \
 && apt-get update \
 && apt-get install -y mssql-server mssql-server-fts \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

EXPOSE 1433

# sqlservr reads ACCEPT_EULA, MSSQL_PID and MSSQL_SA_PASSWORD from the environment
# on first start (set in docker-compose.yml).
CMD ["/opt/mssql/bin/sqlservr"]
