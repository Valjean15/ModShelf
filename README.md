# ModShelf

ModShelf is a cross-platform desktop app for Windows, macOS and Linux that downloads mods from several mod sites and tracks their updates in one place. Nexus Mods is the first source; mod.io, Modrinth and CurseForge can follow as separate source projects.

ModShelf downloads and manages files. **It never installs mods**: it doesn't extract into game folders, deploy, manage load order, detect conflicts, run FOMOD installers or install collections.

> **Status:** Phase 3, the Nexus Mods source. The command line downloads and resumes files, and links a Nexus Mods account. Downloading from Nexus Mods comes next. The desktop app opens an empty window.

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

The headless host in `src/ModShelf.Cli` builds the same dependency-injection container as the desktop app and drives the engine directly:

```powershell
dotnet run --project src/ModShelf.Cli -- download <link> --to <folder> [--name <file>]
dotnet run --project src/ModShelf.Cli -- mod show <link> [--all] [--changelog]
dotnet run --project src/ModShelf.Cli -- mod search [words] --game <game> [--author <name>] [--adult] [--sort <order>] [--page <n>]
dotnet run --project src/ModShelf.Cli -- queue list
dotnet run --project src/ModShelf.Cli -- account list
dotnet run --project src/ModShelf.Cli -- account link <source> [--with api-key|browser|email]
dotnet run --project src/ModShelf.Cli -- account unlink <source>
```

- `download` queues the file and shows its progress until it's done. Put the link in quotes, since shells treat characters such as `&` specially. To check the file against a hash, end the link with `#sha256=<hex>` (or `md5`, `sha1`, `sha512`); the fragment is never sent to the server.
- Ctrl+C stops the download and keeps the partial file. Running the same command again continues where it stopped, including after a crash.
- When a site wants a download started on its website, `download` opens the page in your browser, shows it, and asks for the link the site then hands over. Paste it and press Enter, and the download carries on. Ctrl+C leaves the download waiting; running the same command again shows the page again.
- **Nexus Mods downloads:** `download` takes a mod's page (its newest main file), a file's link from the mod's Files tab, or an `nxm://` link, and needs a linked account. Premium accounts download straight away. Free accounts start each download on the website with "Slow download"; the website then hands your browser an `nxm://` link to paste. Until ModShelf registers itself for these links, the browser can't open them, and usually shows the link it couldn't open: Chrome and Edge in the developer tools' console, Firefox in its address bar. If Vortex or Mod Organizer 2 handles `nxm://` links on your computer, they get the link instead. `download "nxm://…" --to <folder>` on its own also finishes a download waiting in that folder.
- `mod show` looks a mod up from a link to it or to one of its files, and prints its details and its files by section, newest first, each with the link to download it. The file the link points to is marked. `--all` adds old versions, archived files and files in no section; `--changelog` adds the changelog. Nothing is downloaded.
- `mod search` lists a game's mods whose names hold the words, 20 to a page, with author, endorsements, downloads, last update and the link `mod show` and `download` take. The game is named as in the site's links, such as `stardewvalley` for Nexus Mods. `--author` keeps one author's mods, `--adult` includes mods with adult content, `--sort` orders by `Relevance` (the default), `Endorsements`, `Downloads`, `Updated` or `Newest`, and `--page` picks the page.
- `queue list` shows every saved job without starting any download.
- `account list` shows the sources that support accounts, the account linked on each, and the ways to link one.
- `account link` asks for what the source needs. An API key isn't shown as you type it. For `browser`, it opens the site's sign-in page and waits until you approve ModShelf there; if no browser opens, it shows the page to open by hand. Linking again replaces the account.
- `account unlink` removes the account and deletes its stored credentials.
- **Nexus Mods:** `account link nexus` asks for your personal API key, from [your API keys page](https://next.nexusmods.com/settings/api-keys), and shows the account's name and level (Premium, Supporter or Free). Signing in through the browser is built, but Nexus Mods has to issue ModShelf a client ID first; to try it with one, set `ModShelf__Nexus__ClientId`, and `account link nexus --with browser` becomes available. Never commit a client ID.
- Exit codes: 0 done, 1 failed or refused, 2 bad input, 3 still waiting to be started on the site, 130 stopped with Ctrl+C.
- Jobs and linked accounts live in the library database, `library.db` under the user's local application data folder (`%LOCALAPPDATA%\ModShelf` on Windows). To use another file, for example while testing, set the environment variable `ModShelf__DatabasePath`.
- Credentials never go in the database. They go to the system's secret store: DPAPI-encrypted files on Windows, the Keychain on macOS, and the Secret Service keyring on Linux. Without a keyring, Linux falls back to files encrypted for this computer, and the account commands warn about it. To keep test credentials apart from your own, set `ModShelf__SecretScope` to another name (the Keychain and the keyring) and `ModShelf__SecretsFolder` to another folder (the files).
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
  ModShelf.Cli.Tests              runs the CLI end to end: stop, kill and resume a download; finish one through a handoff; look up and search mods; link, refresh and unlink fake accounts
  Shared                          code several test projects compile in: SourceContractTests, TempFolder, the log recorders
```

Dependencies point inward toward Domain, and each mod site lives in its own source project that sees only `Sources.Abstractions`. The architecture tests fail, locally and in CI, if a project gains a reference it isn't allowed to have. See [ADR 0001](docs/adr/0001-layered-architecture.md) for the rules and the reasoning behind them. Where files, types and members go inside each project is in [docs/code-organization.md](docs/code-organization.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. The working rules for people and coding agents are in [CLAUDE.md](CLAUDE.md).

## License

[MIT](LICENSE)
