# Architecture

This page describes how ModShelf is built as of 1.0.7, and why. It replaces the 96 decision records (ADRs) written
while building 1.0: their decisions are summarized here, each section naming the ADRs it draws on. The full records
are listed in [adr/README.md](adr/README.md) and kept in git history at tag `v1.0.6`.

## What ModShelf is, and isn't

ModShelf is a cross-platform desktop app that downloads mods from several mod sites and tracks their updates in one
place. It ships as a desktop app (Avalonia), a command line (`ModShelf.Cli`) and an MCP server for AI agents, which
the command line runs. All three use the same library, settings and accounts.

**It downloads and manages files. It never installs mods**: no extracting into game folders, deploying, load order,
conflict detection, FOMOD installers or collection installs, not even as an option. It never hosts or redistributes
mod files either.

Sites today: **Nexus Mods**, **GameBanana**, **mod.io**, **DEG Mods**, and plain web links. More can be added as
one project each, such as Modrinth or CurseForge (see [adding-a-source.md](adding-a-source.md)).

## Technology

| Area | What's used |
|---|---|
| Runtime | .NET 10 (`net10.0`), C# latest; the SDK is pinned in `global.json` |
| UI | Avalonia 12.1 with the Fluent theme and the Inter font |
| MVVM | CommunityToolkit.Mvvm (source-generated properties and commands) |
| Composition | Microsoft.Extensions.Hosting and dependency injection: one container shape for the desktop app and the CLI |
| Command line | System.CommandLine |
| HTTP | `IHttpClientFactory` with Microsoft.Extensions.Http.Resilience (retries, timeouts, backoff per site) |
| Data | SQLite through EF Core 10, in write-ahead (WAL) mode |
| Agents | The official MCP SDK (`ModelContextProtocol.Core`, and `.AspNetCore` for the HTTP address) |
| DEG Mods | NBitcoin.Secp256k1, to check the Nostr events' signatures |
| Secrets | DPAPI (`ProtectedData`) on Windows, the Keychain on macOS, the Secret Service on Linux |
| Packaging | Velopack: installers, delta updates and self-update; MinVer for versions from git tags |
| Tests | xUnit v3 on Microsoft.Testing.Platform, WireMock.Net, `FakeTimeProvider` |
| CI | GitHub Actions on Windows, macOS and Linux |

Package versions live only in `Directory.Packages.props`. Every project builds with nullable references, warnings as
errors, the .NET analyzers at `latest-recommended`, code style enforced, and deterministic output.

## Layers and dependency rules

*ADRs 0001, 0031–0033.*

Every dependency points inward, toward `Domain`. Each mod site lives in its own project that sees only
`Sources.Abstractions`, so a site can be added or removed without touching the core.

| Project | May reference | Holds |
|---|---|---|
| `Domain` | nothing | Entities and value objects: download jobs, library mods and files, updates, game and mod references |
| `Sources.Abstractions` | `Domain` | `IModSource` and the capability interfaces a site implements |
| `Application` | `Domain`, `Sources.Abstractions` | Use cases, and the ports (interfaces) other layers implement |
| `Sources.<Name>` | `Sources.Abstractions` only | One mod site each: Nexus, GameBanana, ModIo, DegMods, DirectUrl |
| `Infrastructure` | `Application` | EF Core and SQLite, HTTP file transfer, settings and state files, log files, the picture cache |
| `Platform` | `Application` | The operating system: secret stores, link schemes, single instance, tray, notices, Steam, self-update |
| `Presentation` | `Application` | ViewModels, navigation and dialog interfaces. No Avalonia |
| `Desktop` | everything | Avalonia views and styles; composition root of the app |
| `Cli` | everything | The commands and the MCP server; composition root of the command line |

- **Ports live where they're used.** Application defines what it needs (`ILibraryRepository`, `IFileTransfer`,
  `ISecretStore`, `INotificationService`, `IInstalledGames` and so on, in each feature's `Ports/` folder), and
  Infrastructure, Platform or a site implements it.
- **Constructor injection only.** Nothing looks services up outside the two composition roots.
- **The architecture tests enforce it**, locally and in CI: the references above, a project's public surface (what
  implements things shows other projects only what they need), and that the installer stays out of the data folder.
- Where files, types and members go inside a project is in [code-organization.md](code-organization.md).

## Sites and their capabilities

*ADRs 0006, 0007, 0012–0017, 0026, 0028, 0036, 0048, 0053, 0058, 0062–0064, 0087–0093.*

A site implements only the small capability interfaces its website supports. The app asks the source registry which
sites can do what, and offers only that, in each site's own words (such as "endorsements" on Nexus Mods and "likes" on
GameBanana).

| Capability | What it does | Nexus | GameBanana | mod.io | DEG Mods | Web links |
|---|---|:-:|:-:|:-:|:-:|:-:|
| `ILinkRecognizer` | Recognizes a link to a mod or file | ✓ | ✓ | ✓ | ✓ | ✓ (catch-all) |
| `IDownloadResolver` | Turns a file into a short-lived download ticket, or asks for a handoff | ✓ | ✓ | ✓ | ✓ | ✓ |
| `IModCatalog` | A game, its list of games, a mod, its files | ✓ | ✓ | ✓ | ✓ | |
| `IModSearch` | A game's mods a page at a time, with the orders and filters the site has | ✓ | ✓ | ✓ | ✓ | |
| `ITrendingMods` | Browse's **Trending** row | ✓ | ✓ | ✓ | | |
| `IGameSearch` | Finds games by name, for a site with too many to list | | ✓ | | | |
| `IUpdateFeed` | Which of a game's mods changed since a time, in one request | ✓ | | ✓ | ✓ | |
| `IModRequirements` | What a mod needs | ✓ | | ✓ | | |
| `IModCollections` | A collection's files, read as a list to import | ✓ | | | | |
| `IAccountLinker` | Links an account; its `IAuthStrategy`s are the ways to link one | ✓ | ✓ (profile) | ✓ | | |
| `IWatchlistReader` / `IWatchlistSync` | Reads the mods followed on the site; `Sync` also follows and unfollows | Sync | Reader | Reader | | |
| `IApiQuota` | The requests left, from the site's answers | ✓ | | | | |
| `IHandoffScheme` | The link scheme the website hands downloads over with | `nxm` | | | | |

- **Links:** a site's own match wins over the catch-all for web links; two sites claiming a link is a conflict.
- **IDs never change:** games are named by their site and key (`GameRef`, written `nexus/stardewvalley`,
  `gamebanana/16951`, `modio/6715`, `degmods/fallout: new vegas`). The same game on two sites is two games, each with
  its own mods, keys and settings.
- **Download links are never cached**, stored or logged; they expire.
- **Every source passes `SourceContractTests<T>`**, the shared contract in `tests/Shared`.

### Each site

- **Nexus Mods** — the v1 REST API, plus GraphQL v2 for mods' details, thumbnails, games' artwork and requirements.
  Every request carries `Application-Name` and `Application-Version`, and the source backs off before the daily and
  hourly limits. Linked with a personal API key; signing in through the browser (OAuth with PKCE and a loopback
  redirect) is built but stays off until Nexus Mods issues ModShelf a client ID. Premium accounts download directly;
  free accounts start each download on the website and the `nxm://` link hands it back. Tracked mods sync both ways.
- **GameBanana** — needs no account. A member's profile can be linked, read-only, to list the mods it subscribes to.
  Too many games to list, so games are found by GameBanana's own search (a word of 3 letters or more). No update
  feed, so each watched mod is asked. Its files sit side by side: an archived file is the old version of the newer
  file of its name, and other new main files are "new files" rather than updates.
- **mod.io** — needs an API key or a personal access token (the token also lists subscriptions, read-only; ModShelf
  never subscribes, since games install what's subscribed). At most 60 requests a minute, and none while mod.io asks
  to slow down. Files flagged malicious by its virus scan are refused; the MD5 is checked.
- **DEG Mods** — mods are Nostr events on its relays, each signature checked. Files are on Blossom servers, named by
  their SHA-256, and taken from a server other than DEG Mods' own. Files elsewhere (MediaFire, Google Drive, Mega)
  open in the browser and come back through the browser's downloads folder. Hidden or blocked mods, and any mod
  sexualizing minors, are left out. Game covers come from the games database its admin publishes (desktop only).
- **Web links** — any `http(s)` file, saved to a folder of your own, never into the library. A `#sha256=` (or `md5`,
  `sha1`, `sha512`) fragment checks the file and is never sent.

## Downloads

*ADRs 0002–0004, 0014, 0024, 0027, 0091.*

- **A job is a state machine** (`DownloadJob`, in Domain): it allows only its listed transitions, between queued,
  resolving, awaiting a handoff, downloading, verifying, paused, failed, completed and cancelled; any other call throws
  and leaves the job unchanged.
- **One loop owns the queue.** `DownloadScheduler` is a background service reading one channel of commands (enqueue,
  pause, resume, retry, cancel, list) and reports from running transfers. Only the loop changes jobs or saves them, so
  there are no locks and every saved state is whole. Pause and cancel complete once the transfer has really stopped.
  Progress is published at most once per interval and never saved; status changes always are. On start, jobs a crash
  left mid-way are queued again; on stop, running downloads are requeued to go on next time.
- **The partial file is the resume state.** `HttpFileTransfer` writes to `<file>.part`, unbuffered, and resumes with
  a `Range` request, handling servers that ignore or misreport ranges. An expired link (401, 403, 404, 410) keeps the
  partial file and asks the site for a new ticket. The resilience pipeline retries a request that fails before its
  body starts; once the body streams, a dropped or stalled connection is resumed with a new `Range` request, a few
  times, before the job fails. A file that won't fit on the disk fails before writing.
- **One process runs the queue.** It holds `<database>.downloads.lock`. Another process (the CLI, an MCP server)
  lists the queue from the database, refuses changes, and retries the lock every 2 seconds. While the desktop app
  runs, MCP servers ask it to download instead.
- **Handoffs:** a site that wants a download started on its website leaves the job waiting. Nexus Mods hands it back
  with an `nxm://` link (pasted, or opened by ModShelf once it handles the scheme). Other sites' pages hand back a
  file: ModShelf watches the browser's downloads folder only while a job waits, takes the file by name or SHA-256,
  checks it, and moves it into place. **Choose the file…** hands one over by hand.
- **Hashes** are checked whenever the site gives one; a mismatch leaves the file where it was and keeps waiting.

## The library

*ADRs 0005, 0018–0020, 0034, 0042, 0044, 0045, 0056, 0057, 0067, 0076, 0081–0083, 0098.*

- **One SQLite database**, `library.db` in the user's local application data folder, holds the jobs, the library
  and the linked accounts. EF Core migrations shape it (nine so far, `InitialCreate` to `AddPinsAndNotes`); a
  committed migration is never edited. Since 1.0.1 it runs in WAL mode, so the app, the CLI and agents read while
  another process writes. Database calls run off the caller's thread, and any command taking a second or more is
  logged, with what it was but never its values.
- **`LibraryMod`** is a mod you watch, have files of, or both. **`LibraryFile`** is a downloaded file: name, version
  label, section, upload time, size, SHA-256 and path, relative to its game's folder.
- **Where files go:** under the library root, or a game's own folder, by a pattern such as the default
  `{source}/{game}/{mod} ({modid})/{file}`, per game if set. Names are made safe on every system; a clash adds the
  file's ID. A mod keeps its folder when the site renames it.
- **Old versions** follow the site's chains of which file replaced which (`ModFile.ReplacedBy`): Nexus Mods' update
  links, GameBanana's archived files, mod.io's upload order; DEG Mods has none. A link only counts forward in time.
  The rule keeps the newest 2 by default, or all, or moves them to `_archive`; old versions leave the database in the
  same change that records the new file, and the disk after it.
- **Moving** the library or a game's files never overwrites a file, and puts every file back if one can't move.
- **Rescan** finds files placed by hand (by name, and size within 1 KB) and hashes files that have none.
- **History:** each download, archive, deletion, file found and mod removed is a `LibraryEvent`, kept for a year.
- **Pins, skipped updates and notes** belong to the mod. **Forget** removes a mod and leaves its files on disk.
  **Undo** restores a mod exactly as a change found it, in one transaction, never touching the disk.
- **Lists** (`IModLists`): the library exported as a `modshelf-list` JSON file of mods and files by the sites' IDs,
  and imported back after a plan made from the library alone: new mods watched, with the list's pins and notes, and the
  listed files the library lacks queued. A site's collection (`IModCollections`, Nexus Mods' so far) reads as such a
  list: its files at the versions it pins, what it needs from elsewhere named, never downloaded. Nothing is installed,
  and a collection's install steps are never run.

## Finding updates

*ADRs 0017, 0021, 0022, 0034, 0043, 0051, 0057, 0059.*

- **Version labels are never compared.** An update is the newest file of a library file's chain that the library
  doesn't have; for a main file without a chain, a newer main file; for a mod that's only watched, a main file uploaded
  since the last check. A mod with files also gets **new optional files** uploaded since the last check.
- **Asking as little as possible:** a site with an update feed is asked once per game for what changed within its
  reach (28 days); only the changed mods are then read. A mod never checked, or last checked beyond the reach, gets a
  full check. Sites without a feed are asked mod by mod.
- **Pinned mods' updates and skipped updates** are still found but not offered; a skip lasts until a newer file comes.
- The desktop app checks every `updates.interval` (6 hours by default, or per game) and shows a notice for new ones.
  A notice that can't be shown is logged, never thrown.
- Before work that would likely run out a site's requests, the app says so and asks; a strip warns when fewer than
  `quota.warnBelow` percent are left.

## Accounts and secrets

*ADRs 0007–0011, 0013, 0028, 0073, 0077, 0094.*

- **Credentials never go in the database or settings.** They go through `ISecretStore`: DPAPI-encrypted files on
  Windows (the secret's name mixed into the encryption), the Keychain on macOS, the Secret Service on Linux, falling
  back to files encrypted for the computer when there's no keyring, with a warning.
- **`AccountService`** links, unlinks and refreshes, with one lock per site, so a refresh never overwrites an account
  linked again meanwhile. Credentials are stored as versioned JSON.
- **The `LinkedAccounts` table** keeps who is linked, how, their level and avatar: never a secret, which a test checks
  column by column.
- **Sources never see the secret store.** Each site's named `HttpClient` gets a handler that adds the linked account
  to its requests, and refreshes once when the site refuses it.

## Settings and remembered state

*ADRs 0019, 0037, 0055, 0063, 0066, 0067, 0072, 0074.*

- **A value the user sets on purpose is a setting**, in `settings.json` beside the database. **A value the app
  remembers for them is state**, in `ui-state.json` (the window, the sidebar, the screen last open, recent searches,
  Browse's game and site). Deprecated MCP calls are counted in `deprecated-calls.json`.
- `settings.json` allows comments and trailing commas, keeps keys it doesn't know, and a file it can't read is an
  error naming the file.
- **Games have their own settings** (folder pattern, root folder, old versions, update checks and interval, notices,
  previews, Browse's start, hidden), each falling back to every game's. Installed games' matches to sites' games are
  kept by store and ID, such as `steam/489830`.
- Every setting is listed in [settings.md](settings.md).

## The desktop app

*ADRs 0023–0026, 0029, 0037, 0046, 0047, 0050–0055, 0065, 0069–0075, 0084, 0092, 0094–0097.*

- **Presentation** holds every ViewModel, built with CommunityToolkit.Mvvm and tested without a UI thread;
  navigation, dialogs and the clipboard are interfaces the views implement. **Desktop** holds the Avalonia views, with
  compiled bindings and code-behind for view logic only. User-facing strings are in `.resx` resources.
- **Screens:** Library (by game, then a game's mods, then a mod's page), Updates, Browse (the globe in the sidebar;
  the magnifier is quick search), Downloads, and Settings, which holds the accounts. Browse lists nothing until a game
  is chosen, then opens on the game chosen last. Light and dark themes come from one set of brushes in `Styles/Theme.axaml`, with text
  contrast of at least 4.5:1.
- **Shared pieces:** one name matcher (`NameQuery`) for every box that finds games or mods by name, so "stalker 2"
  finds S.T.A.L.K.E.R. 2; one game picker for Browse and Settings, which offers the games Steam installed first; quick
  search (Ctrl+K); site names shown only once two sites are in use; an undo bar, which can undo a change made to
  several selected mods at once; a quota strip.
- **Big libraries stay quick:** list rows are virtualized, and cards are grouped in rows (`CardRows`) built only as
  they scroll into view. Lists that sit inside a scrolling page, such as Settings › Games' hundreds of games, scroll
  on their own so they can be virtualized too, and read a game's cover once its row shows. Pictures and mods' details are cached (`pictures` folder, 512 MB by default, least recently
  shown first) so screens fill at once and offline.
- **One app per library:** a second start brings the running window forward through a named pipe, and hands it any
  `nxm://` link. Closing can keep the app in the tray. A first-run setup covers the library folder, an account and
  `nxm://` links.
- **Installed games:** Platform reads Steam's own library folders and manifests (where the registry says on Windows,
  `~/Library/Application Support/Steam` on macOS, Flatpak, XDG and Snap folders on Linux), writing nothing; Application
  matches them to the sites' games by name, or as chosen.

## The command line

*ADRs 0006, 0011, 0027, 0031, 0035, 0085.*

- A System.CommandLine host that builds the same services as the desktop app, without the views. Commands are
  listed in [command-line.md](command-line.md).
- Exit codes: 0 done, 1 failed or refused, 2 bad input, 3 waiting on the site, 70 unexpected, 130 stopped.
- Several `ModShelf.Cli` processes at once are normal (each agent's app starts its own); the queue lock and WAL
  keep them from blocking each other or the app.
- `diagnostics` writes what a bug report needs, with no keys, names or links.

## The MCP server and agents

*ADRs 0040, 0041, 0059–0061, 0068, 0080, 0085.*

- `ModShelf.Cli mcp` serves MCP over standard input and output. While agents are on and the desktop app runs, it
  also serves `http://127.0.0.1:47823/mcp` (Streamable HTTP, `agents.port`), answering this computer only.
- **Off until the user turns it on** (`agents.enabled`, read at every call). An agent can't change that, link
  accounts, delete files, move the library or take a site's links.
- 28 tools (`library_*`, `download` and `downloads_*`, `mod_games`, `mod_show`, `mod_search`, `settings_*`,
  `accounts_status`, `diagnostics`), each with an output schema and annotations (read-only, changing, destructive,
  reaching the sites). Results include their nulls, so clients that validate against the schema accept them.
- Arguments that don't fit a tool's schema fail before it runs, saying what it takes. A renamed argument keeps
  working until the next major version, with a `deprecated` block in the result, a warning in the log and a count
  that `diagnostics` reports.
- Settings › **Agents** copies a setup prompt, shows the configuration, and can add the server to Claude Desktop's
  configuration, keeping a backup. See [mcp-server.md](mcp-server.md).

## Logging

*ADRs 0027, 0035, 0081, 0086.*

- `ILogger<T>` with source-generated `[LoggerMessage]` methods in each class's `<Class>.Log.cs`, each area with its
  own block of event IDs.
- **Never logged:** tokens, API keys, `nxm://` keys, full download URLs, or the CLI's option values (only their
  names). Polly's retry attempts stay out unless they're warnings.
- ModShelf writes its own daily files (`desktop-YYYYMMDD.log`, `cli-YYYYMMDD.log`) in a `logs` folder beside the
  database, keeping a week. Each run starts with its version, process ID and the process that started it.

## Installers and self-update

*ADRs 0035, 0036, 0038, 0079.*

- `build/pack.ps1` and `build/pack.sh` publish the app self-contained and pack it with Velopack: a per-user
  `Setup.exe` and portable zip for `win-x64`, an AppImage for `linux-x64`, and a `.pkg` and zip for macOS (packed on
  a Mac). The command line ships beside the desktop app, so it updates with it.
- The app installs apart from its data (`%LOCALAPPDATA%\ModShelf.App` vs `%LOCALAPPDATA%\ModShelf` on Windows), so
  uninstalling keeps the library. On Windows, uninstalling gives `nxm://` links back to the app that had them.
- The installed app looks for a newer GitHub release at start (not pre-releases), downloads it when asked, and
  applies it on **Restart to update**. A build from source doesn't update itself.
- Versions come from git tags through MinVer: `v1.0.6` is 1.0.6, and commits after it are `1.0.7-alpha.0.N`.

### Releases

- Pushing a tag such as `v1.0.7` runs `.github/workflows/release.yml`: tests on the three systems, a check that the tag
  is the version MinVer gives, release notes from the `feat:` and `fix:` commits since the last release, the packages
  for `win-x64`, `linux-x64` and `osx-arm64`, and a **draft** release. Installed apps see it once it's published by
  hand. **Run workflow** does everything but the release.
- Every released database and `settings.json` is upgraded in the tests, from 0.9.0 on.
- Steps are in [development.md](development.md#releasing); the checks to make by hand are the manual checklists.

## Testing

- One test project per library, plus `ModShelf.Architecture.Tests` (the rules above, packaging, release notes) and
  `ModShelf.Cli.Tests`, which runs the command line and the MCP server end to end.
- **Tests never call a live site.** Each site's answers are recorded once and replayed with WireMock.Net. Live
  accounts are only used for the [manual checklists](manual-checklist.md) before a release.
- Time comes from `TimeProvider` (tests use `FakeTimeProvider`); paths are built with `Path.Combine`, never assuming
  a case-insensitive file system; CI runs on Windows, macOS and Linux.
- Behaviour changes are checked by planting deliberate bugs and confirming the tests catch them; survivors lead to
  new tests.

## Known limits

- Signing in to Nexus Mods through the browser waits for Nexus Mods to issue a client ID; API keys work today.
- The installers aren't signed, so Windows SmartScreen and macOS Gatekeeper warn the first time.
- Releases are built for Windows x64, Linux x64 and macOS on Apple silicon.
- macOS shows notices through Script Editor for now. Linux needs a tray host for the tray icon.
- The AppImage's command line moves each time it starts, so agents on Linux use the HTTP address.
- Installed games are read from Steam only.
- Update feeds reach back 28 days; GameBanana has none, so its watched mods are asked one by one.
- Only Nexus Mods hands page downloads back by link; other sites' come through the browser's downloads folder.
