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

### Command line

The headless host in `src/ModShelf.Cli` builds the same dependency-injection container as the desktop app and drives the download engine directly:

```powershell
dotnet run --project src/ModShelf.Cli -- download <link> --to <folder> [--name <file>]
dotnet run --project src/ModShelf.Cli -- queue list
```

- `download` queues the file and shows its progress until it's done. Put the link in quotes, since shells treat characters such as `&` specially. To check the file against a hash, end the link with `#sha256=<hex>` (or `md5`, `sha1`, `sha512`); the fragment is never sent to the server.
- Ctrl+C stops the download and keeps the partial file. Running the same command again continues where it stopped, including after a crash.
- `queue list` shows every saved job without starting any download.
- Exit codes: 0 done, 1 failed, 2 bad input, 130 stopped with Ctrl+C.
- Jobs live in the library database, `library.db` under the user's local application data folder (`%LOCALAPPDATA%\ModShelf` on Windows). To use another file, for example while testing, set the environment variable `ModShelf__DatabasePath`.
- Logs go to stderr and show warnings only; set `Logging__LogLevel__Default=Debug` for more.

## Solution layout

```
src/
  ModShelf.Domain                 entities and value objects, no dependencies
  ModShelf.Sources.Abstractions   IModSource and the capability interfaces a source implements
  ModShelf.Application            use cases and ports
  ModShelf.Infrastructure         storage, HTTP file transfer, settings, logging
  ModShelf.Platform               OS integration (secret stores, nxm:// handler, notifications)
  ModShelf.Sources.Nexus          the Nexus Mods source
  ModShelf.Sources.DirectUrl      plain web links, with no mod site involved
  ModShelf.Presentation           ViewModels, no Avalonia reference
  ModShelf.Desktop                Avalonia views and the desktop composition root
  ModShelf.Cli                    headless composition root
tests/
  ModShelf.<Library>.Tests        one test project per library
  ModShelf.Architecture.Tests     enforces the dependency rules
  ModShelf.Cli.Tests              runs the built CLI end to end: stop, kill and resume a download
  Shared/SourceContractTests.cs   checks every source's test project compiles in
```

Dependencies point inward toward Domain, and each mod site lives in its own source project that sees only `Sources.Abstractions`. The architecture tests fail, locally and in CI, if a project gains a reference it isn't allowed to have. See [ADR 0001](docs/adr/0001-layered-architecture.md) for the rules and the reasoning behind them.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. The working rules for people and coding agents are in [CLAUDE.md](CLAUDE.md).

## License

[MIT](LICENSE)
