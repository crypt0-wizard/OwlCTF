# Development

This guide covers a local OwlCTF setup. Production hosts should use the [deployment guide](deployment.md).

## Requirements

- .NET 10 SDK
- MariaDB 10.6 or newer
- A Discord OAuth application
- Docker Engine only when testing dynamic challenge instances

## Database and Discord

Create an empty MariaDB database and an account that can create and alter its schema. OwlCTF initializes and upgrades the schema during startup.

The EF Core migration check runs at startup by default, but only calls `MigrateAsync` when a migration is pending. Set `Database:ApplyEfMigrationsOnStartup` to `false` in local configuration if you manage EF migrations separately.

Add this callback to the Discord application:

~~~text
https://localhost:7125/signin-discord
~~~

## Local secrets

Keep credentials outside the repository:

~~~powershell
dotnet user-secrets --project src/OwlCTF.Web/OwlCTF.Web.csproj set "Discord:ClientId" "..."
dotnet user-secrets --project src/OwlCTF.Web/OwlCTF.Web.csproj set "Discord:ClientSecret" "..."
dotnet user-secrets --project src/OwlCTF.Web/OwlCTF.Web.csproj set "Security:FlagPepper" "a-random-secret-with-at-least-32-characters"
dotnet user-secrets --project src/OwlCTF.Web/OwlCTF.Web.csproj set "ConnectionStrings:MariaDb" "Server=localhost;Port=3306;Database=owlctf;User ID=owlctf;Password=..."
~~~

Machine-specific non-secret settings can go in src/OwlCTF.Web/appsettings.Local.json, which Git ignores. User secrets, environment variables, and command-line values override checked-in configuration.

## Run

~~~powershell
dotnet run --project src/OwlCTF.Web/OwlCTF.Web.csproj
~~~

Open https://localhost:7125. The first account to sign in becomes the initial administrator.

## Tests

Run the complete suite:

~~~powershell
dotnet test OwlCTF.slnx
~~~

The suite covers scoring, team rules, flag ownership, instance expiry, event timing, Discord integration, content sanitization, uploads, and branding.

For interface changes, also verify:

- Light and dark themes
- Desktop and narrow mobile widths
- Keyboard focus and form validation
- Empty, loading, and populated states where applicable

## Dynamic challenge instances

Dynamic instances require a reachable Docker Engine and are disabled by default. Each instance receives a team-owned randomized flag, a random host port, resource limits, and a fixed lifetime.

Treat Docker daemon access as host-level access. Do not expose an unauthenticated Docker TCP API. Production setup and capacity guidance live in [Deployment: dynamic challenge instances](deployment.md#dynamic-challenge-instances).

## Public scoreboard feed

`GET /api/v1/scoreboard` returns the live standings in the minimal JSON feed format accepted by CTFtime. `GET /api/v1/ctftime/standings` is an alias for the same response. Both routes are public and use the same filtered ranking data as the standings page.

~~~json
{
  "standings": [
    {
      "pos": 1,
      "team": "Packet Owls",
      "score": 1250,
      "lastAccept": 1788030360
    }
  ]
}
~~~

`lastAccept` is omitted until a team records its first solve. The older `GET /api/v1/standings` route remains available for clients that use OwlCTF's native standing fields.

## Project conventions

- Keep controllers focused on HTTP, authorization, validation, and view selection.
- Put application rules and integrations in services.
- Keep schema startup changes safe to run repeatedly on existing databases.
- Add tests for business rules, scoring, security decisions, and lifecycle behavior.
- Preserve public routes and stored data unless a compatibility change is deliberate and documented.

See [Architecture](architecture.md) for the full boundary map.
