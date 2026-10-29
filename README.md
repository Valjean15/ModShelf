# ModShelf

ModShelf is a cross-platform desktop app for Windows, macOS and Linux that downloads mods from several mod sites and tracks their updates in one place. Nexus Mods is the first source; mod.io, Modrinth and CurseForge can follow as separate source projects.

ModShelf downloads and manages files. **It never installs mods**: it doesn't extract into game folders, deploy, manage load order, detect conflicts, run FOMOD installers or install collections.

> **Status:** Phase 5, the desktop app. The command line downloads files from Nexus Mods and from plain web links, keeps a library of the mods you download or watch, checks them for updates, and links a Nexus Mods account. The desktop app has its six screens in a sidebar, of which Library, Updates, Browse and Downloads work so far, and does the background work while it's open: queued downloads, recording them into the library, and update checks with a notice of new updates.

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

To see a real notice on this computer, as the desktop app will show for new updates, run this one test with `MODSHELF_TEST_NOTICES` set to 1. It fails if the notice couldn't be shown, and says why.

```powershell
dotnet build
$env:MODSHELF_TEST_NOTICES = "1"
tests/ModShelf.Platform.Tests/bin/Debug/net10.0/ModShelf.Platform.Tests -method ModShelf.Platform.Tests.Notifications.NotificationServicesTests.ShowsARealNotice
```

### Desktop app

`dotnet run --project src/ModShelf.Desktop` opens the main window: a sidebar of the screens, Library, Updates, Browse, Downloads, Accounts and Settings, and the one selected. The button at the top of the sidebar hides the screens' names. The screens fill in over Phase 5. While the app is open, queued downloads go on, finished ones go into the library, and the watched mods are checked for updates every `updates.interval`. It uses the same library database, settings, accounts and environment variables as the command line, so a library made with one shows in the other.

The Library screen lists the mods you downloaded or watch, by game, with a box to filter them by name. Each mod opens its page or its folder, and can be watched or unwatched; expanded, it lists its files, each of which can be deleted from disk, after asking. The Updates screen lists the updates found for the watched mods, with **Check now**, and for each update **Download**, which puts it into the library beside the file it replaces, and **Changes**, its version's entry in the mod's changelog. Both follow the library as it changes, and the badge on Updates counts the updates.

The Browse screen searches a game's mods on Nexus Mods, which needs a linked account. Pick the game from the box, which lists the site's games, and search by words or author, in one of nine orders, with or without adult content; **Load more** lists the next page. A link to a mod in the search box opens that mod. Clicking a mod opens it: its details, its files by section, with **Download** into the library and **Save to a folder…** for each, and its changelog. It can be watched in the library, and tracked on Nexus Mods. **Tracked mods** lists the mods of the game you track there.

The Downloads screen takes a link to a mod or a file: **Download** puts it into the library, and **Save to a folder…** into a folder you pick. It lists every download, newest first, with its progress, speed and time left, and the buttons to pause, resume, retry or cancel it. Cancelling a download with bytes saved asks first. **Clear finished** removes the done and cancelled downloads from the list; their files stay. A download Nexus Mods wants started on its website shows **Open on Nexus Mods** and **Paste link…**: start it there with the download for mod managers, copy the `nxm://` link its button holds, and paste it. The badge on Downloads counts the downloads waiting for you, to be started or because they failed.

### Command line

The headless host in `src/ModShelf.Cli` builds the same dependency-injection container as the desktop app and drives the engine directly:

```powershell
dotnet run --project src/ModShelf.Cli -- download <link> [--to <folder> [--name <file>]]
dotnet run --project src/ModShelf.Cli -- mod show <link> [--all] [--changelog]
dotnet run --project src/ModShelf.Cli -- mod search [words] --game <game> [--author <name>] [--adult] [--sort <order>] [--ascending] [--page <n>] [--page-size <count>]
dotnet run --project src/ModShelf.Cli -- mod updates --game <game> [--since 1d|7d|28d|<date>]
dotnet run --project src/ModShelf.Cli -- mod tracked
dotnet run --project src/ModShelf.Cli -- mod track <link>
dotnet run --project src/ModShelf.Cli -- mod untrack <link>
dotnet run --project src/ModShelf.Cli -- library list
dotnet run --project src/ModShelf.Cli -- library watch <link>
dotnet run --project src/ModShelf.Cli -- library unwatch <link>
dotnet run --project src/ModShelf.Cli -- library check
dotnet run --project src/ModShelf.Cli -- library updates [--changelog]
dotnet run --project src/ModShelf.Cli -- library move <folder>
dotnet run --project src/ModShelf.Cli -- settings show
dotnet run --project src/ModShelf.Cli -- settings set <setting> <value>
dotnet run --project src/ModShelf.Cli -- settings reset <setting>
dotnet run --project src/ModShelf.Cli -- queue list
dotnet run --project src/ModShelf.Cli -- account list
dotnet run --project src/ModShelf.Cli -- account link <source> [--with api-key|browser|email]
dotnet run --project src/ModShelf.Cli -- account unlink <source>
```

- `download` queues the file and shows its progress until it's done. Put the link in quotes, since shells treat characters such as `&` specially. A file that won't fit on the disk fails before anything is written.
- **Into the library:** without `--to`, the file goes under the library root, in its mod's folder, and the library records it, with its mod watched for updates. The file keeps the name its site gives it, made safe on every system; when another file has that name, the file's ID is added to it. A file the library has, still on disk, isn't downloaded again. Only files of mods on sites such as Nexus Mods go into the library.
- **Old versions:** the files a download replaced, as its site says (Nexus Mods' update chains), are its old versions. By default, the two newest versions stay and older ones are **deleted for good**; `library.oldVersions` can keep them all, or move them to an `_archive` folder instead. `download` says what it moved or deleted.
- **Into a folder of your own:** `--to` saves any file there, such as one from a plain web link, and `--name` picks its name. To check the file against a hash, end the link with `#sha256=<hex>` (or `md5`, `sha1`, `sha512`); the fragment is never sent to the server.
- Ctrl+C stops the download and keeps the partial file. Running the same command again continues where it stopped, including after a crash.
- When a site wants a download started on its website, `download` opens the page in your browser, shows it, and asks for the link the site then hands over. Paste it and press Enter, and the download carries on. Ctrl+C leaves the download waiting; running the same command again shows the page again.
- **Nexus Mods downloads:** `download` takes a mod's page (its newest main file), a file's link from the mod's Files tab, or an `nxm://` link, and needs a linked account. Premium accounts download straight away. Free accounts start each download on the website with "Slow download"; the website then hands your browser an `nxm://` link to paste. Until ModShelf registers itself for these links, the browser can't open them, and usually shows the link it couldn't open: Chrome and Edge in the developer tools' console, Firefox in its address bar. If Vortex or Mod Organizer 2 handles `nxm://` links on your computer, they get the link instead. `download "nxm://…"` on its own also finishes a download waiting for it: in the library, or with `--to`, in that folder.
- `mod show` looks a mod up from a link to it or to one of its files, and prints its details and its files by section, newest first, each with the link to download it. The file the link points to is marked. `--all` adds old versions, archived files and files in no section; `--changelog` adds the changelog. Nothing is downloaded.
- `mod search` lists a game's mods whose names hold the words, a page at a time, with author, endorsements, downloads, last update and the link `mod show` and `download` take. The game is named as in the site's links, such as `stardewvalley` for Nexus Mods. `--author` keeps one author's mods, and `--adult` includes mods with adult content. `--sort` orders by `Relevance` (the default), `Endorsements`, `Downloads`, `Updated` or `Newest`, the most first; `--ascending` puts the least first, such as the oldest. `--page` picks the page, and `--page-size` how many mods it lists, from 1 to 100 (20 unless set).
- `mod updates` lists a game's mods that changed since a time, in one request: each with when its files last changed, when it was last active, and its link. Mods with new or changed files since then are marked; the others only changed their pages, such as their descriptions or comments. `--since` takes `1d` (the default), `7d` or `28d`, or a date or time such as `2026-10-20` or `2026-10-20T18:00`, in local time unless it gives an offset. Nexus Mods lists the last 28 days only.
- `mod tracked` lists the mods your linked account tracks on the site. `mod track` and `mod untrack` track the mod a link is to, or stop tracking it, as the site's Track button does; doing either twice changes nothing.
- When more than one site can search, list updates or track mods, `--source` picks one, such as `--source nexus`.
- `library list` shows the library: the mods ModShelf watches for updates, and the ones with downloaded files, each with its link. `library watch` adds the mod a link is to and watches it; `library unwatch` stops watching it, and a mod without downloaded files leaves the library. Watching is ModShelf's own, apart from a site's tracked mods.
- `library check` checks the watched mods for updates now, and lists the new ones with the link to download each. An update is the newest file of a downloaded file's update chain on the site, or, for a main file the author didn't link, a newer main file; for a mod that's only watched, a main file uploaded since the last check. Version labels are never compared. It asks the site as little as it can: once per game for the mods checked in the last 28 days, then only about mods whose files changed. `library updates` lists every update found; `--changelog` adds what changed in each. Downloading an update takes it off the list.
- `library move` moves the library's files to the same places under another folder, which becomes the library's root. It waits until downloads into the library are finished, never moves a file over another, and puts every file back if one can't move. Ctrl+C stops it; running the same command again finishes it. Your own files under the old root stay, and the folders it emptied are removed.
- `settings show` lists the settings, marking the defaults, and says where they're kept. `settings set` changes one, and `settings reset` puts it back to its default:
  - `library.root` is the library's folder: a ModShelf folder in your Downloads folder unless set. Once the library has files, `library move` changes it instead, taking them along.
  - `library.folders` is where files go under it, as folders separated by `/` and ending in `{file}`. The folders mix text with `{source}`, `{game}`, `{mod}` and `{modid}`, such as the default, `{source}/{game}/{mod} ({modid})/{file}`. A mod keeps its folder when its site renames it.
  - `library.folders.<game>` is a pattern for one game, named as in the site's links, such as `library.folders.skyrimspecialedition`.
  - `library.oldVersions` is what happens to a file's old versions when a newer one is downloaded: `all` keeps them, `archive` moves them to an `_archive` folder in their folder, and a number keeps that many versions, the newest first, and deletes the others. The default is `2`.
  - `updates.interval` is how often the desktop app will check the watched mods for updates: minutes, hours or days, such as `30m`, `6h` or `1d`, from 15 minutes to 7 days. The default is `6h`. The command line checks only when asked.
  - `updates.notify` is whether a background check that finds new updates shows a notice: `true`, the default, or `false`. Windows shows it as a toast from ModShelf, macOS through Script Editor for now, and Linux through libnotify, which most desktops have. The command line only prints what `library check` finds.
- `queue list` shows every saved job without starting any download.
- `account list` shows the sources that support accounts, the account linked on each, and the ways to link one.
- `account link` asks for what the source needs. An API key isn't shown as you type it. For `browser`, it opens the site's sign-in page and waits until you approve ModShelf there; if no browser opens, it shows the page to open by hand. Linking again replaces the account.
- `account unlink` removes the account and deletes its stored credentials.
- **Nexus Mods:** `account link nexus` asks for your personal API key, from [your API keys page](https://next.nexusmods.com/settings/api-keys), and shows the account's name and level (Premium, Supporter or Free). Signing in through the browser is built, but Nexus Mods has to issue ModShelf a client ID first; to try it with one, set `ModShelf__Nexus__ClientId`, and `account link nexus --with browser` becomes available. Never commit a client ID.
- Exit codes: 0 done, 1 failed or refused, 2 bad input (arguments the command line can't read too), 3 still waiting to be started on the site, 70 something ModShelf didn't expect (said in a line, with the details in the log), 130 stopped with Ctrl+C.
- Jobs, the library and linked accounts live in the library database, `library.db` under the user's local application data folder (`%LOCALAPPDATA%\ModShelf` on Windows), and the settings in `settings.json` beside it. To use another database, for example while testing, set the environment variable `ModShelf__DatabasePath`; the settings move with it.
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
  ModShelf.Cli.Tests              runs the CLI end to end: stop, kill and resume a download; finish one through a handoff; download into the library, move it and check it for updates; look up and search mods; link, refresh and unlink fake accounts
  Shared                          code several test projects compile in: SourceContractTests, TempFolder, the log recorders
```

Dependencies point inward toward Domain, and each mod site lives in its own source project that sees only `Sources.Abstractions`. The architecture tests fail, locally and in CI, if a project gains a reference it isn't allowed to have. See [ADR 0001](docs/adr/0001-layered-architecture.md) for the rules and the reasoning behind them. Where files, types and members go inside each project is in [docs/code-organization.md](docs/code-organization.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. The working rules for people and coding agents are in [CLAUDE.md](CLAUDE.md).

## License

[MIT](LICENSE)
