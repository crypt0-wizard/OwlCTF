# Security

OwlCTF handles challenge secrets, team activity, account identities, and IP addresses. Treat an event deployment as security-sensitive infrastructure.

## Secrets

- Never commit Discord credentials, database passwords, real flags, webhook URLs, FLAG_PEPPER, or data-protection keys.
- Generate independent, high-entropy values for every production installation.
- Keep FLAG_PEPPER and data-protection keys stable across restarts and upgrades.
- Rotate exposed credentials immediately; removing them from a later commit is not enough.

## Network exposure

- Publish only Caddy on ports 80 and 443.
- Keep MariaDB, Redis, Kestrel, and the Docker API on private networks.
- Accept forwarded client information only from the trusted reverse proxy.
- Keep the application and base images patched and rehearse upgrades before an event.

The supplied Compose stack applies read-only filesystems, bounded resources, dropped capabilities where practical, and private backend networks. See [Deployment](deployment.md) for operational details.

## Dynamic challenge containers

Docker socket access is effectively control of the host. Enable dynamic instances only on a dedicated, trusted machine and use the supplied override deliberately.

- Never expose the Docker API over unauthenticated TCP.
- Apply conservative CPU, memory, process, renewal, and lifetime limits.
- Keep the global instance cap within measured host capacity.
- Use challenge images you trust and rebuild them from reviewed sources.
- Separate multi-host challenge infrastructure from the public web tier when the event requires stronger isolation.

## Flags and competition integrity

- Static flags are pepper-hashed.
- Dynamic flags are randomized, hashed, and tied to the team that received them.
- Cross-team flag matches create an incident and notify administrators.
- Automatic banning is optional; review incidents before enabling it for a live event.
- Banned and flagged teams are blocked by middleware rather than individual endpoints.

## Sensitive event data

Submission logs include attempted flags, identities, teams, outcomes, IP addresses, and timestamps. Limit administrator access, retain logs only as long as needed, and handle exports as sensitive data.

Backups may contain the same data. Protect, test, and expire them according to the event's policy.

## Reporting a vulnerability

Do not include working flags, credentials, private event data, or unnecessary exploit detail in a public issue. Contact the maintainers privately first and allow time for a fix before public disclosure.
