# Container deployment

The included Compose stack is intended for a single Linux Docker host. Caddy is the only service with published ports; OwlCTF, MariaDB, and Redis remain on private Docker networks.

By default, OwlCTF checks for pending EF Core migrations at startup and applies them only when needed. The up-to-date check is inexpensive and no migration lock is taken when nothing is pending. For controlled or multi-replica releases, run database migrations as a separate deployment step and set `APPLY_DATABASE_MIGRATIONS=false` before starting the web replicas. Keep it enabled for straightforward single-host installations.

## Prepare the host

1. Point the event domain's A and AAAA records at the host.
2. Allow inbound TCP 80 and TCP/UDP 443.
3. Copy `.env.example` to `.env` and replace every credential placeholder.
4. Add `https://your-domain/signin-discord` to the Discord application's redirect URLs.

The first account to sign in becomes the initial administrator.

Compose supplies database credentials Discord credentials and the flag pepper through environment variables. Base application settings do not contain fallback secrets. For a deployment outside Compose provide `ConnectionStrings__MariaDb` `Discord__ClientId` `Discord__ClientSecret` and `Security__FlagPepper` through the host environment or a secret manager.

Every service on the backend network has a fixed address. This prevents a service that starts early from being auto-assigned Caddy's address before Caddy starts. If `172.30.0.0/24` overlaps an existing network, change `BACKEND_SUBNET` and update `CADDY_PROXY_IP` `WEB_BACKEND_IP` `MARIADB_BACKEND_IP` and `REDIS_BACKEND_IP`. Keep all four addresses unique and inside the chosen subnet. `ReverseProxy__KnownProxy` reads `CADDY_PROXY_IP` directly, so forwarded-header trust stays aligned with Caddy.

## Start and inspect

```sh
docker compose config
docker compose up -d --build
docker compose ps
```

Caddy automatically provisions HTTPS when the configured domain resolves to the host and ports 80 and 443 are reachable. Its certificate state is stored in `caddy-data` and must be preserved.

To validate a changed Caddyfile before deployment:

```sh
docker compose run --rm --no-deps caddy caddy validate --config /etc/caddy/Caddyfile
```

The Caddyfile is baked into the small Caddy image, avoiding fragile single-file bind mounts. Rebuild the Caddy service after editing it:

```sh
docker compose build caddy
docker compose up -d --no-deps caddy
```

## Scale the web tier

The base stack uses one web container with a fixed backend address. Do not pass `--scale web=...` to the base Compose file because replicas cannot share that address. For multiple web replicas, create a deployment override with separately named web services and give each one a unique backend address. Caddy discovers their addresses through Docker DNS and Redis carries SignalR events between replicas.

Keep the database pool budget below MariaDB's connection limit. A safe estimate is:

```text
web replicas × WEB_DB_POOL_SIZE + 20 <= DB_MAX_CONNECTIONS
```

The default database limits leave room for additional web services when they are defined with unique addresses. Increase memory and MariaDB's buffer pool before increasing connection counts. On multi-host deployments, replace the local named volumes and single database/Redis services with shared storage and managed or clustered equivalents.

## Resource and security defaults

- The web and Caddy root filesystems are read-only.
- Writable uploads, challenge files, keys, database state, and certificates use dedicated volumes.
- Linux capabilities are removed where possible and container process counts, memory, CPU, logs, and shutdown time are bounded.
- Redis is private, ephemeral, and used only for real-time message distribution.
- MariaDB is not published to the host.
- The application accepts forwarded client details only from Caddy's configured private address.

Tune the `*_CPUS`, `*_MEMORY_LIMIT`, pool, and database values in `.env` based on measurements from a rehearsal with realistic teams and challenges.

## Dynamic challenge instances

Docker daemon access effectively grants host-level control, so it is excluded from the base stack. On a dedicated, trusted challenge host, determine the socket group ID:

```sh
stat -c '%g' /var/run/docker.sock
```

Set that value as `DOCKER_GID`. Then configure the platform-wide instance
and enforcement switches:

```env
DYNAMIC_INSTANCES_ENABLED=true
AUTO_BAN_ON_CHEAT=false
```

`DYNAMIC_INSTANCES_ENABLED` is the platform-wide switch. A challenge must
also have its own Docker instance option enabled in challenge management.

Keep `AUTO_BAN_ON_CHEAT=false` unless the event policy explicitly calls for
automatic enforcement. With it disabled, cross-team instance-flag matches
are still recorded as incidents and sent to the configured administrator
webhook; administrators can review them before taking action.

Start with the restricted override:

```sh
docker compose -f compose.yaml -f compose.instances.yaml up -d --build
```

Do not expose the Docker API over an unauthenticated TCP socket. Keep the instance concurrency limit within the host's tested CPU and memory capacity.

## Backups and upgrades

Back up `mariadb-data`, `app-data`, `uploads-data`, and `caddy-data`. Keep `FLAG_PEPPER`, Discord credentials, database credentials, and data-protection keys stable.

Before upgrading:

```sh
docker compose pull
docker compose build --pull
docker compose up -d
docker compose ps
```

Check `/health/ready` through the public domain and inspect bounded container logs with `docker compose logs --tail=200`.

The web image also checks `http://127.0.0.1:8080/health/ready` from inside its own container. Production host filtering allows the configured public domain plus `localhost` `127.0.0.1` and the internal `web` service name so both this probe and Caddy requests pass host validation.
