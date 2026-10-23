# Contributing to ModShelf

The rules for this repository live in [CLAUDE.md](CLAUDE.md). They apply to people and coding agents alike, so read that file before your first change. In short:

- Work on the current phase of `docs/implementation-plan.md` only, and keep each change to one task.
- Put new files where [docs/code-organization.md](docs/code-organization.md) says. The build fails when a namespace doesn't match its folder.
- Respect the dependency rules. The architecture tests enforce them; never weaken a test to make a build pass.
- Before you open a pull request, `dotnet build` must finish with 0 warnings and `dotnet test` must be green. CI checks both on Windows, macOS and Linux.
- Every behavior change comes with tests. Tests never call live mod-site APIs.
- Never commit secrets such as API keys or OAuth tokens. Use `dotnet user-secrets` or environment variables for local testing.
- Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`), one logical change per commit.
- Record significant design decisions as an ADR in `docs/adr/NNNN-short-title.md`.
- Ask before adding a project or package, changing a public interface in `Sources.Abstractions` or `Application`, or changing the database schema.
- Change the schema only through a new EF Core migration: run `dotnet tool restore` once, then `dotnet ef migrations add <Name> --project src/ModShelf.Infrastructure`. Never edit a migration that's already committed.
