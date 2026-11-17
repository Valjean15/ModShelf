# Building, testing and releasing

This page is for working on ModShelf itself. The rules for changes, for people and coding agents alike, are in
[CONTRIBUTING.md](../CONTRIBUTING.md) and [CLAUDE.md](../CLAUDE.md).

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

The tests never call a live mod site. Before a release, someone runs the manual checklists against Nexus Mods with a Premium and a free account, against GameBanana, without an account and with a profile linked, against mod.io with an API key and with an access token, and against DEG Mods: [manual-checklist.md](manual-checklist.md) for the command line, and [manual-checklist-desktop.md](manual-checklist-desktop.md) for the desktop app. [manual-checklist-installer.md](manual-checklist-installer.md) installs the app on each system and updates it from one version to the next.

To see a real notice on this computer, as the desktop app will show for new updates, run this one test with `MODSHELF_TEST_NOTICES` set to 1. It fails if the notice couldn't be shown, and says why.

```powershell
dotnet build
$env:MODSHELF_TEST_NOTICES = "1"
tests/ModShelf.Platform.Tests/bin/Debug/net10.0/ModShelf.Platform.Tests -method ModShelf.Platform.Tests.Notifications.NotificationServicesTests.ShowsARealNotice
```

## Project layout

```
build/                            packing the installers (pack.ps1, pack.sh), the release notes, the macOS Info.plist, and the icons
src/
  ModShelf.Domain                 entities and value objects, no dependencies
  ModShelf.Sources.Abstractions   IModSource and the capability interfaces a source implements
  ModShelf.Application            use cases and ports
  ModShelf.Infrastructure         storage, HTTP file transfer, settings, logging
  ModShelf.Platform               OS integration (secret stores, nxm:// handler, notifications)
  ModShelf.Sources.Nexus          the Nexus Mods source
  ModShelf.Sources.GameBanana     the GameBanana source
  ModShelf.Sources.ModIo          the mod.io source
  ModShelf.Sources.DegMods        the DEG Mods source
  ModShelf.Sources.DirectUrl      plain web links, with no mod site involved
  ModShelf.Presentation           ViewModels, no Avalonia reference
  ModShelf.Desktop                Avalonia views and the desktop composition root
  ModShelf.Cli                    headless composition root: the commands, and the MCP server
tests/
  ModShelf.<Library>.Tests        one test project per library
  ModShelf.Architecture.Tests     enforces the dependency rules, keeps the installer out of ModShelf's data folder, and checks the release notes
  ModShelf.Cli.Tests              runs the CLI end to end: stop, kill and resume a download; finish one through a handoff; download into the library, move it and check it for updates; look up and search mods; link, check, refresh and unlink fake accounts; and the MCP server's tools, with a client
  Shared                          code several test projects compile in: SourceContractTests, TempFolder, the log recorders
```

Dependencies point inward toward Domain, and each mod site lives in its own source project that sees only `Sources.Abstractions`. The architecture tests fail, locally and in CI, if a project gains a reference it isn't allowed to have. See [ADR 0001](architecture.md#layers-and-dependency-rules) for the rules and the reasoning behind them. Where files, types and members go inside each project is in [docs/code-organization.md](code-organization.md).

Adding a mod site is [adding-a-source.md](adding-a-source.md); how ModShelf is built, and why, is
[architecture.md](architecture.md).

## Packing the installers

`build/pack.ps1` (PowerShell) and `build/pack.sh` (Bash) pack the desktop app's installers and update packages with [Velopack](https://velopack.io), whose `vpk` the scripts restore as a local tool, into `artifacts/releases`:

```powershell
./build/pack.ps1                                          # this system's, at the version MinVer gives
./build/pack.ps1 -Version 0.9.1 -Notes notes.md win-x64   # a version and its notes, for one runtime
```

Each runtime packs a self-contained publish: `win-x64` a per-user `Setup.exe` with a Start-menu shortcut, `linux-x64` an AppImage (which needs `mksquashfs`, and `zstd` for a delta package), and `osx-arm64` or `osx-x64` an app in a `.pkg`, on a Mac only. The installers aren't signed, which needs a certificate bought for the purpose: the first time, Windows SmartScreen warns about `Setup.exe` (**More info**, then **Run anyway**) and macOS Gatekeeper about the `.pkg` (Control-click, **Open**). Windows installs into `%LOCALAPPDATA%\ModShelf.App`, apart from `%LOCALAPPDATA%\ModShelf`, where the library stays when ModShelf is uninstalled. [ADR 0038](architecture.md#installers-and-self-update) has the details. The command line, `ModShelf.Cli`, is in the same folder as the desktop app, so it updates with it: `%LOCALAPPDATA%\ModShelf.App\current\ModShelf.Cli.exe` on Windows, and `ModShelf.app/Contents/MacOS/ModShelf.Cli` on macOS. The AppImage holds it too, but starts only the desktop app ([ADR 0040](architecture.md#the-mcp-server-and-agents)). `build/render-icons.py` renders the Windows and macOS icons from `modshelf.svg`.

## Releasing

Releasing a version is tagging its commit, such as `v1.0.1`, and pushing the tag:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

The release workflow, `.github/workflows/release.yml`, then tests the commit on the three systems, checks that the tag is the version MinVer gives it, writes the release notes from the `feat:` and `fix:` commits since the release before, packs `win-x64`, `linux-x64` and `osx-arm64`, and uploads them to one **draft** release, "ModShelf 1.0.1": a release, or a pre-release for a version with a suffix, such as 1.1.0-beta.1, or one of 0.x. The installed app takes releases alone; the 0.9 betas took pre-releases too, so they update to 1.0.0. A pre-release is tried with the installer on its release's page. Installed apps see nothing until the draft is published: check its notes and files on the Releases page, then publish it. **Run workflow** on the Actions tab does all of it but the release, and keeps the packages with the run for 14 days, to download and try. [ADR 0039](architecture.md#releases) has the details; `build/release-notes.sh` shows the notes a tag would get.
