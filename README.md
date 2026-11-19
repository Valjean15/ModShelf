# ModShelf

[![Latest release](https://img.shields.io/github/v/release/Valjean15/ModShelf)](https://github.com/Valjean15/ModShelf/releases/latest)
[![CI](https://github.com/Valjean15/ModShelf/actions/workflows/ci.yml/badge.svg)](https://github.com/Valjean15/ModShelf/actions/workflows/ci.yml)

**One place to download your mods and keep them up to date.**

ModShelf is a free desktop app for Windows, macOS and Linux. It downloads mods from several mod sites into one tidy
library on your disk, remembers which version of each file you have, and tells you when there's an update.

> ModShelf downloads and manages files. **It never installs mods**: no extracting into game folders, load orders or
> installers. That stays with you or your mod manager.

## What it does

- **Downloads from several sites:** Nexus Mods, GameBanana, mod.io, DEG Mods and Thunderstore, plus plain web links.
- **Keeps a library:** each file goes into a folder for its game and mod, with its version and SHA-256. Old versions
  are kept, archived or cleaned up, as you choose, and **Free up space** finds archived versions, duplicates and
  leftovers to delete, keeping any file that may be the only copy left.
- **Finds updates:** checks the mods you watch every few hours and shows what changed. Pin a mod, skip an update, or
  update everything at once.
- **Browse and search:** find a game's mods on each site, with filters, and download several at once.
- **Knows your games:** the games Steam installed on your PC come first when you pick one.
- **Works your way:** use the desktop app, the command line, or an AI agent such as Claude through the built-in MCP
  server, which stays off until you turn it on.

## Install

Download the latest version from the [Releases page](https://github.com/Valjean15/ModShelf/releases/latest):

| System | Download |
|---|---|
| Windows (x64) | the `Setup.exe` installer, or the portable `.zip` |
| macOS (Apple silicon) | the `.pkg` installer, or the portable `.zip` |
| Linux (x64) | the `.AppImage` |

The installers aren't signed yet, so the first time your system warns about them. On Windows, choose **More info**,
then **Run anyway**. On macOS, Control-click the `.pkg` and choose **Open**. After that, ModShelf updates itself
whenever a new version is released.

## Getting started

1. **Open ModShelf.** A short setup asks where to keep your library and offers to link an account.
2. **Link your accounts** in **Settings › Accounts**. Nexus Mods and mod.io need one (an API key works). GameBanana,
   DEG Mods and Thunderstore work without one.
3. **Find a mod** on the **Browse** screen: pick a game, search, and choose **Download**. You can also paste any link on
   the **Downloads** screen.
4. **Stay up to date** on the **Updates** screen. ModShelf checks the mods you watch every 6 hours; choose **Download**
   for one update or **Update all**.

With a free Nexus Mods account, each download starts on the Nexus Mods website. Turn on **Settings › Links** so
ModShelf picks up the download as soon as you click the website's button.

## Documentation

| Page | What's in it |
|---|---|
| [Desktop app](docs/desktop-app.md) | Every screen, in detail |
| [Command line](docs/command-line.md) | Every `ModShelf.Cli` command |
| [MCP server](docs/mcp-server.md) | Letting an AI agent work with your library |
| [Settings and data](docs/settings.md) | Every setting, where your data is kept, and environment variables |
| [Building, testing and releasing](docs/development.md) | Working on ModShelf itself |
| [Architecture](docs/architecture.md) | How ModShelf is built, and why |
| [Adding a source](docs/adding-a-source.md) | Adding another mod site |
| [Code organization](docs/code-organization.md) | Where files, types and members go |
| Manual checklists: [command line](docs/manual-checklist.md), [desktop](docs/manual-checklist-desktop.md), [installers](docs/manual-checklist-installer.md), [MCP server](docs/manual-checklist-mcp.md) | What to check by hand before a release |

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). From the repository root:

```powershell
dotnet build                                 # 0 warnings, 0 errors
dotnet test                                  # every test project
dotnet run --project src/ModShelf.Desktop    # opens the desktop app
```

More in [Building, testing and releasing](docs/development.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. The working rules for people and coding agents are in
[CLAUDE.md](CLAUDE.md).

## License

[MIT](LICENSE)
