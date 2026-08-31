# Contributing to OwlCTF

Thanks for helping improve OwlCTF.

## Before you start

- Search existing issues before opening a new one.
- Keep changes focused. Separate refactoring from behavior changes when practical.
- Never commit credentials, flags, uploaded files, data-protection keys, or event data.
- Preserve existing routes and database behavior unless the change explicitly requires a compatibility break.

## Development workflow

1. Create a branch from the current default branch.
2. Configure local secrets using the [development guide](docs/development.md).
3. Make the smallest coherent change.
4. Run `dotnet test OwlCTF.slnx`.
5. For UI changes, check both light and dark themes at desktop and mobile widths.

## Code style

The repository uses the standard ASP.NET Core MVC layout and the rules in `.editorconfig`. Prefer clear names and small, single-purpose types. Comments should explain decisions or constraints, not restate the code.

New behavior should include tests when it contains business rules, security decisions, scoring logic, or lifecycle transitions.

## Pull requests

Describe the problem, the chosen approach, and how the change was verified. Call out schema changes, new configuration keys, and deployment considerations explicitly.
