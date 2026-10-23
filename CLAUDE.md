# ModShelf — Agent Instructions

These rules apply to every agent working in this repository. Read this file fully before making changes. If a request conflicts with these rules, stop and ask the human instead of guessing.

## The product

ModShelf is a cross-platform desktop app (Avalonia 12 on .NET 10) that downloads mods from multiple sources and tracks their updates in one place. Nexus Mods is the first source. Every layer must stay ready for more sources, such as mod.io, Modrinth and CurseForge.

**ModShelf downloads and manages files. It never installs mods.** It does not extract into game folders, deploy, manage load order, detect conflicts, run FOMOD installers or install collections. This applies even to "optional" features.

The implementation plan in `docs/implementation-plan.md` is the source of truth for scope and phase order. The plan was drafted under the placeholder name "ModHub", so read every `ModHub` as `ModShelf`. Work on the current phase only, unless the human asks otherwise.

## Environment

- Repo root: `D:\Repository backups\ModShelf`. The path contains a space, so always quote it in commands and scripts.
- The primary dev machine runs Windows, but all code must run on Windows, macOS and Linux. CI builds and tests on all three.
- The default shell is PowerShell. Bash snippets in the plan work in Git Bash.
- The SDK is pinned by `global.json` (.NET 10).

Common commands, run from the repo root:

```powershell
dotnet build              # must finish with 0 warnings, 0 errors
dotnet test               # all tests must pass
dotnet run --project src/ModShelf.Desktop
dotnet run --project src/ModShelf.Cli -- download <url> --to <folder>
```

## Solution layout

| Project | Responsibility |
|---|---|
| `ModShelf.Domain` | Entities and value objects. No dependencies. |
| `ModShelf.Sources.Abstractions` | `IModSource` and the capability interfaces every source implements |
| `ModShelf.Application` | Use cases and ports (interfaces): download scheduler, update checker, account service |
| `ModShelf.Infrastructure` | EF Core + SQLite, HTTP file transfer, settings, logging |
| `ModShelf.Platform` | OS integration: secret stores, `nxm://` registration, single instance, notifications |
| `ModShelf.Sources.<Name>` | One project per mod site (first: `ModShelf.Sources.Nexus`) |
| `ModShelf.Presentation` | ViewModels, plus navigation and dialog interfaces. No Avalonia reference. |
| `ModShelf.Desktop` | Avalonia views, styles, startup, composition root |
| `ModShelf.Cli` | Headless host for testing, composition root |
| `tests/ModShelf.*.Tests` | One test project per library, plus `ModShelf.Architecture.Tests` and `ModShelf.Cli.Tests`; helpers they share live in `tests/Shared` |

## Dependency rules (enforced by architecture tests)

- `Domain` references nothing.
- `Sources.Abstractions` references `Domain`.
- `Application` references `Domain` and `Sources.Abstractions`.
- `Sources.<Name>` references `Sources.Abstractions` only. It never references `Application`, `Infrastructure`, `Platform` or another source.
- `Infrastructure`, `Platform` and `Presentation` reference `Application`.
- `Desktop` and `Cli` reference everything, for DI registration only.
- Only `Desktop` references Avalonia.

Never add a reference that breaks these rules. Never weaken or skip an architecture test to make a build pass. If a feature seems to need a forbidden reference, the interface probably belongs in a lower layer, so propose that instead.

## Design principles

- **SOLID.** Capability interfaces stay small (`IModCatalog`, `IModSearch`, `IDownloadResolver`, `IUpdateFeed`, `IAccountLinker`, `IWatchlistSync`). A source implements only what its site supports.
- **Ports live where they are used.** Application defines the interfaces; Infrastructure, Platform and sources implement them.
- **Adding a source means one new project**, plus DI registration and game mappings. If adding a source requires changing Domain, Application or Presentation, stop and raise it, because the abstraction is wrong.
- **Use constructor injection only.** No service locator (`Ioc.Default`, `IServiceProvider` lookups) outside the composition roots.

## Coding conventions

- Put files, types and members where `docs/code-organization.md` says: feature folders, the namespace matching the folder, `Ports/` for what Application needs from other layers, `Common/` for helpers several features share, member order.
- C# latest, nullable enabled, file-scoped namespaces, one type per file.
- Classes are `sealed` unless designed for inheritance. Use `record` or `record struct` for value objects and DTOs.
- Async all the way: `Async` suffix, `CancellationToken` as the last parameter, no `.Result` or `.Wait()`.
- Get the time from `TimeProvider`, never from `DateTime.Now` or `DateTime.UtcNow` directly.
- Build paths with `Path.Combine` and validate names with `Path.GetInvalidFileNameChars()`. Never hard-code `\` or `/`, and never assume a case-insensitive file system.
- Make HTTP calls through `IHttpClientFactory` and the resilience pipeline, never `new HttpClient()`.
- Log through `ILogger<T>` with structured `[LoggerMessage]` methods in `<Class>.Log.cs`. Never log tokens, API keys, `nxm://` keys or full download URLs.
- ViewModels use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`). Views use compiled bindings (`x:DataType`), and code-behind holds only view logic.
- Put user-facing strings in `.resx` resources, never hard-coded in views or ViewModels.
- Package versions live only in `Directory.Packages.props`.
- Don't disable warnings-as-errors or suppress analyzer rules. If a suppression is truly needed, scope it narrowly and justify it in a comment.

## Mod-site APIs and secrets

- Use official APIs only. Never scrape website HTML.
- Send the app name and version headers on every Nexus request. Respect rate-limit headers and back off before hitting the limits.
- Never cache download links, because they expire.
- Never commit secrets: API keys, OAuth tokens, or client IDs tied to a personal account. For local testing, use `dotnet user-secrets` or environment variables.
- At runtime, secrets go through `ISecretStore`, never into SQLite or settings files.
- Tests and CI never call live APIs; use recorded fixtures with WireMock.Net. The human does live-account testing manually.

## Testing

- Every behavior change comes with tests in the matching test project.
- Each source inherits `SourceContractTests<T>`.
- ViewModels are tested without the UI thread.
- Never delete or weaken an existing test to make it pass. Fix the code, or ask.

## Workflow

1. Read this file and the relevant plan section before starting.
2. Keep each change scoped to one task. No drive-by refactors, and no reformatting of unrelated files.
3. Before saying a task is done, run `dotnet build` with zero warnings and get `dotnet test` all green.
4. Use Conventional Commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`), with one logical change per commit.
5. **Never push, force-push, rewrite history or change remotes.** The human owns GitHub. Commit locally only when asked.
6. Record significant design decisions as an ADR in `docs/adr/NNNN-short-title.md`.
7. Finish with a short summary: what changed, which files, how it was tested, and anything left open.

## Ask before

- Adding a new project or NuGet package, or upgrading a package
- Changing a public interface in `Sources.Abstractions` or `Application`
- Changing the database schema. Add a new EF Core migration; never edit an existing one.
- Anything that deletes user files, touches the `nxm://` handler registration, or touches credential storage
- Anything that looks like installing mods
