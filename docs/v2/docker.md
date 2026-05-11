# VRCGT v2 -- Docker Deployment

## Overview

Docker support is an **optional, advanced deployment mode** intended for
self-hosted users who want to run VRCGT as a background service (e.g. on a
home server or NAS) without keeping a desktop session open.

> **Normal users should use the standalone desktop build.**
> See [packaging.md](packaging.md) for the one-click launcher that requires no
> Docker, no Node.js, and no manual server management.

---

## Quick start

```bash
# 1. Clone the repo
git clone https://github.com/yourorg/VRCGT.git
cd VRCGT

# 2. Start the app (builds the image on first run)
docker compose -f docker/docker-compose.yml up -d

# 3. Open the web UI
# http://localhost:45721
```

The first run builds the Docker image (~3-5 min on a cold cache).
Subsequent starts are instant because Docker caches the build layers.

---

## Prerequisites

| Tool | Minimum version |
|------|----------------|
| Docker Engine | 24+ |
| Docker Compose | v2 (the `docker compose` plugin, not `docker-compose` v1) |

Node.js and .NET are **not** required on the host — they are only used
inside the builder stage of the Docker image.

---

## Architecture

The Docker container runs **VRCGT.Server** directly:

```
Container
  VRCGT.Server (ASP.NET Core 8 / Kestrel)
    -- listens on 0.0.0.0:45721 (inside container)
    -- serves wwwroot/ (built React UI)
    -- runs EF Core migrations at startup
    -- stores data in /app/data (mounted volume)

Host
  127.0.0.1:45721 -> container:45721   (default -- loopback only)
```

VRCGT.Launcher (the browser-opener) is **not** used in Docker mode;
the server runs headlessly and you open the browser yourself.

---

## Environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `VRCGT_DATA_DIR` | `/app/data` | Directory for the database, logs, cache, and exports. Mount a volume here to persist data across restarts. |
| `VRCGT_BIND_URL` | `http://0.0.0.0:45721` | Kestrel listen URL inside the container. The host-side port mapping is controlled by `docker-compose.yml`. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | ASP.NET Core environment. Change to `Development` only for local debugging. |

---

## Data persistence

All persistent state is stored under `/app/data` inside the container.
The `docker-compose.yml` mounts a named Docker volume there so data
survives container restarts and image upgrades:

```
/app/data/
  vrcgt-v2.db           -- SQLite database (users, audit log, group config)
  access-settings.json  -- bind-mode sidecar (Stage 16)
  logs/                 -- rotating application logs
  cache/                -- VRChat API response cache (safe to delete)
  exports/              -- user-initiated CSV/JSON exports
  backups/              -- automatic member-list backups
```

### Backup

To back up your data, copy the named volume contents:

```bash
# Create a tar archive of the data volume
docker run --rm \
  -v vrcgt_vrcgt-data:/data \
  -v "$(pwd)":/backup \
  alpine tar czf /backup/vrcgt-backup.tar.gz -C /data .
```

### Restore

```bash
docker run --rm \
  -v vrcgt_vrcgt-data:/data \
  -v "$(pwd)":/backup \
  alpine tar xzf /backup/vrcgt-backup.tar.gz -C /data
```

---

## Upgrading

```bash
# Pull / rebuild the updated image and restart
docker compose -f docker/docker-compose.yml pull
docker compose -f docker/docker-compose.yml up -d --build
```

EF Core migrations are applied automatically at startup -- no manual
migration step is needed.

---

## Security

### Default configuration (safe)

By default `docker-compose.yml` binds the host port to `127.0.0.1` only:

```yaml
ports:
  - "127.0.0.1:45721:45721"
```

This means the web UI is only reachable from the **same machine** that
runs Docker. Other devices on your network cannot connect.

### Exposing to a local network

To make the UI accessible from other devices on your LAN, change the
port binding to:

```yaml
ports:
  - "45721:45721"   # bind on all host interfaces
```

> **WARNING:** This exposes VRCGT to **every device on your local network**.
> Anyone who can reach your host's IP on port 45721 can access the login page.
> VRCGT enforces password authentication, but:
> - Use a strong password for all local user accounts.
> - Only do this on networks you trust (home LAN, VPN, etc.).
> - Never expose port 45721 directly to the public internet.

### Exposing to the internet (reverse proxy required)

If you need remote access, **do not** expose port 45721 publicly.
Instead, put VRCGT behind a reverse proxy that provides:

- TLS termination (HTTPS)
- Additional authentication (Basic Auth, OAuth, client certificates)
- Rate limiting

Example nginx snippet:

```nginx
location / {
    proxy_pass         http://127.0.0.1:45721;
    proxy_set_header   Host $host;
    proxy_set_header   X-Real-IP $remote_addr;
    proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
    # Required for SignalR
    proxy_http_version 1.1;
    proxy_set_header   Upgrade $http_upgrade;
    proxy_set_header   Connection "upgrade";
}
```

> **The VRChat API credentials stored in VRCGT's database are sensitive.**
> Treat a publicly accessible VRCGT instance with the same care as any
> service that holds account credentials.

### Cookie security

VRCGT uses `HttpOnly`, `SameSite=Strict` session cookies. In the Docker
deployment, cookies are transmitted over HTTP between the browser and
`localhost`. If you use a reverse proxy with HTTPS, set the
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` environment variable so
Kestrel marks cookies as `Secure`.

---

## Troubleshooting

### Container exits immediately

Check the logs:

```bash
docker compose -f docker/docker-compose.yml logs vrcgt
```

### Port already in use

Another process is using port 45721 on the host. Change the host-side port:

```yaml
ports:
  - "127.0.0.1:48000:45721"   # UI available at http://localhost:48000
```

### Database locked / read-only

The `/app/data` volume must be writable by user `vrcgt` (UID 1001).
If you bind-mount a host directory, ensure the permissions allow writing:

```bash
sudo chown -R 1001:0 /your/host/data/path
```

### Resetting all data

```bash
docker compose -f docker/docker-compose.yml down -v
```

This deletes the named volume permanently. All users, sessions, and
audit log data will be lost.
