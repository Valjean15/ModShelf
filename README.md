# ModShelf

ModShelf is a cross-platform desktop app for Windows, macOS and Linux that downloads mods from several mod sites and tracks their updates in one place. Nexus Mods is the first source; mod.io, Modrinth and CurseForge can follow as separate source projects.

ModShelf downloads and manages files. **It never installs mods**: it doesn't extract into game folders, deploy, manage load order, detect conflicts, run FOMOD installers or install collections.

> **Status:** Phase 0, repository and solution setup. The app opens an empty window; no features yet.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.100 or a later 10.0 feature band (pinned in `global.json`)
- An editor with .NET 10 support: Visual Studio 2026, Rider, or VS Code with the C# Dev Kit. Add the Avalonia extension for XAML previews. Visual Studio 2022 doesn't support .NET 10 projects.

## Build, test and run

Run these from the repository root, in PowerShell or any other shell:

```powershell
dotnet build                                 # must finish with 0 warnings, 0 errors
dotnet test                                  # runs every test project in ModShelf.slnx
dotnet run --project src/ModShelf.Desktop    # opens the desktop app
```

`dotnet test` runs on Microsoft.Testing.Platform, which `global.json` selects for the whole repository.

The headless host in `src/ModShelf.Cli` builds the same dependency-injection container as the desktop app. Its first commands (`download`, `queue list`) arrive in Phase 1.

## Solution layout

```
src/
  ModShelf.Domain                 entities and value objects, no dependencies
  ModShelf.Sources.Abstractions   IModSource and the capability interfaces a source implements
  ModShelf.Application            use cases and ports
  ModShelf.Infrastructure         storage, HTTP file transfer, settings, logging
  ModShelf.Platform               OS integration (secret stores, nxm:// handler, notifications)
  ModShelf.Sources.Nexus          the Nexus Mods source
  ModShelf.Presentation           ViewModels, no Avalonia reference
  ModShelf.Desktop                Avalonia views and the desktop composition root
  ModShelf.Cli                    headless composition root
tests/
  ModShelf.<Library>.Tests        one test project per library
  ModShelf.Architecture.Tests     enforces the dependency rules
```

Dependencies point inward toward Domain, and each mod site lives in its own source project that sees only `Sources.Abstractions`. The architecture tests fail, locally and in CI, if a project gains a reference it isn't allowed to have. See [ADR 0001](docs/adr/0001-layered-architecture.md) for the rules and the reasoning behind them.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. The working rules for people and coding agents are in [CLAUDE.md](CLAUDE.md).

## License

[MIT](LICENSE)
