# Architecture

OwlCTF is a modular ASP.NET Core MVC application. It intentionally ships as one deployable web project so event hosts can operate it without coordinating several services.

## Projects

- `src/OwlCTF.Web` contains the web application.
- `tests/OwlCTF.Tests` contains unit and service-level tests.

## Application boundaries

- **Controllers** handle HTTP concerns, authorization, validation, and view selection.
- **Services** contain scoring, flag validation, platform settings, storage, Docker instance lifecycle, and notification behavior.
- **Data** owns MariaDB access, schema initialization, Entity Framework mappings, and migrations.
- **Models** contains persisted projections, view models, and validated input models.
- **Hubs** exposes the SignalR activity stream.
- **Views** and **wwwroot** contain the server-rendered interface and browser assets.

The namespaces remain rooted at `OwlCTF`; moving a file between folders should be accompanied by a namespace change only when it represents a real boundary change.

## Persistence

Most competition data uses Dapper through `AppDb`. Dynamic challenge instance state uses Entity Framework Core because it benefits from entity tracking, migrations, and transactional lease handling. Both paths use the same MariaDB database.

Schema initialization runs when the application starts. Changes must remain safe to run against an existing installation.

## Request flow

1. Forwarded headers and security headers are applied.
2. Authentication restores the Discord-backed session.
3. Team access middleware blocks banned or administratively flagged teams.
4. Authorization and rate limits protect controllers and instance APIs.
5. Controllers call services or data access and return a view or API result.

## Runtime data

`App_Data` holds challenge files and data-protection keys for local development. Uploaded branding and sponsor assets live below `wwwroot/uploads`. These paths are runtime state and are excluded from source control.

Production containers store challenge files and data-protection keys in `app-data`, while uploaded site assets use `uploads-data`. MariaDB and Caddy certificate state use their own named volumes. Redis is deliberately ephemeral because it only carries live SignalR messages.

## Dynamic challenge instances

The instance service reserves one lease per team and challenge, generates a team-owned flag, then asks the Docker runtime to start a constrained container on a random host port. A hosted reaper claims expired leases and removes their containers.

Issued flags are hashed and tied to their owning team. Cross-team submissions create a cheat incident and notify administrators; automatic banning remains an explicit configuration choice.

## Real-time updates

Successful solves invalidate the scoreboard cache and publish an activity event through SignalR. The recent-solves page reconnects automatically when a browser temporarily loses its connection. Container deployments use a Redis backplane so those events reach clients across all web replicas; Caddy provides WebSocket-aware session affinity and dynamic replica discovery.

## Container edge

Caddy is the only container with published ports. It terminates TLS, renews certificates, compresses responses, and proxies requests to private OwlCTF replicas. The application trusts forwarded headers only from Caddy's fixed address on the isolated backend network.

Startup schema work uses a MariaDB advisory lock, expiry processing uses database leases, and first-blood announcements are claimed before delivery. These boundaries allow multiple web replicas to run without racing migrations, container cleanup, or Discord notifications.
