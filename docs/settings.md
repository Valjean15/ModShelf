# Settings and data

ModShelf keeps its settings in `settings.json`, beside the library database. Change them on the desktop app's
Settings screen, or from the command line:

```text
ModShelf.Cli settings show                  # every setting, marking the defaults, and where they're kept
ModShelf.Cli settings set <setting> <value>
ModShelf.Cli settings reset <setting>       # back to its default
```

## Every setting

- `library.root` is the library's folder: a ModShelf folder in your Downloads folder unless set. Once the library has files, `library move` changes it instead, taking them along.
- `library.folders` is where files go under it, as folders separated by `/` and ending in `{file}`. The folders mix text with `{source}`, `{game}`, `{mod}` and `{modid}`, such as the default, `{source}/{game}/{mod} ({modid})/{file}`. A mod keeps its folder when its site renames it.
- `library.folders.<site>/<game>` is a pattern for one game, named by its site and as in the site's links, such as `library.folders.nexus/skyrimspecialedition`. The game's key alone, as `library.folders.skyrimspecialedition`, does while only one site's game in the library or the settings has it, or only one site has games; `settings show` gives the full name.
- `library.oldVersions` is what happens to a file's old versions when a newer one is downloaded: `all` keeps them, `archive` moves them to an `_archive` folder in their folder, and a number keeps that many versions, the newest first, and deletes the others. The default is `2`.
- `updates.interval` is how often the desktop app will check the watched mods for updates: minutes, hours or days, such as `30m`, `6h` or `1d`, from 15 minutes to 7 days. The default is `6h`. The command line checks only when asked.
- `updates.notify` is whether a background check that finds new updates shows a notice: `true`, the default, or `false`. Windows shows it as a toast from ModShelf, macOS through Script Editor for now, and Linux through libnotify, which most desktops have. The command line only prints what `library check` finds.
- `appearance.theme` is whether the desktop app is `light` or `dark`, or follows the system's mode: `system`, the default.
- `window.closeToTray` is whether closing the desktop app's window keeps it running in the tray (`true`) or quits it (`false`). Until it's set, the app asks the first time.
- `setup.done` is whether the desktop app's first-run setup was finished or skipped. `settings reset setup.done` shows it again at the next start of a library with no mods and no account linked.
- `app.checkForUpdates` is whether the installed desktop app looks for a newer ModShelf when it starts: `true`, the default, or `false`, when it looks only when asked, on its Settings screen.
- `previews.library`, `previews.browse`, `previews.updates` and `previews.downloads` are how the desktop app's screens show their mods: `off`, `thumbnails` or `cards`. The defaults are `thumbnails` for Library, `cards` for Browse, and `off` for the others, which show cards as thumbnails.
- `pictures.maxSize` is the most room, in megabytes, mods' saved pictures take, from 64 to 8192: `512` unless set. The ones shown least recently go first.
- `pictures.adult` is how the desktop app shows the pictures of mods with adult content: `hidden`, `blurred`, the default, or `shown`.
- `details.maxAge` is how old mods' saved details get before they're read from their site again: minutes, hours or days, such as `6h` or `7d`, from 15 minutes to 7 days. The default is `1d`.
- `quota.warnBelow` is below what share of a site's requests for the hour or the day the desktop app warns that they run low: a percent from 0, for never, to 90. The default is `10`.
- `downloads.browserFolder` is the folder your browser saves downloads to, where a download waiting on a site's page takes the file the browser saves: a full path, or the system's Downloads folder unless set.
- `agents.enabled` is whether AI agents may use ModShelf through its MCP server: `false`, the default, or `true`. `agents.port` is the port of the address the desktop app serves the server at meanwhile, from 1024 to 65535: `47823` unless set. An agent can't change either.
- `games.<site>/<game>.<setting>` is one game's own setting, in place of every game's, with the game named as for `library.folders.<site>/<game>`, such as `games.nexus/stardewvalley.notify`. The same game on two sites is two games, each with settings of its own. `settings reset` sets it back to every game's. `games.<site>/<game>.folders` is the same as `library.folders.<site>/<game>`, which `settings show` lists. Before 1.0.0, `settings.json` named a game by its key alone; such a game is Nexus Mods', and is named by its site from the next change on. `oldVersions`, `updateInterval` and `notify` take the values `library.oldVersions`, `updates.interval` and `updates.notify` take. `checkUpdates` set to `false` leaves the game out of the background checks, `library check` and the desktop app's Check now; only checking the game alone checks it. `hidden` set to `true` leaves the game out of the desktop app's lists, though its mods are still checked. `root` is a folder of the game's own, a full path, where its files go in place of `library.root`, the folder pattern still saying where below it; it changes this way only while the game has no files, and `library move-game`, or the desktop app's Settings › Games, moves a game with files. Copying another game's settings leaves it as it is. `browse.sort` (an order, as `mod search --sort` takes it, in lower case), `browse.direction` (`ascending` or `descending`), `browse.window` (`1d`, `7d` or `30d`) and `browse.pageSize` (`20`, `50` or `100`) are how the desktop app's Browse screen starts for the game; what the game's site can't search by is refused. `previews` (`off`, `thumbnails` or `cards`) is how the game's mods show on its page in the Library and in Browse while it's chosen, in place of `previews.library` and `previews.browse`.

## Where ModShelf keeps its data

- Jobs, the library and linked accounts live in the library database, `library.db` under the user's local application data folder (`%LOCALAPPDATA%\ModShelf` on Windows), and the settings in `settings.json` beside it, with the count of deprecated MCP calls in `deprecated-calls.json`, and what the desktop app remembers for you, the window's place, the sidebar, the screen last open, the recent searches of Browse and of quick search, and Browse's site, in `ui-state.json`. To use another database, for example while testing, set the environment variable `ModShelf__DatabasePath`; the settings and the count move with it. Since 1.0.1 the database writes ahead of its file (SQLite's WAL), so the desktop app, the command line and agents' MCP servers can read it while another changes it without waiting: while ModShelf runs, `library.db-wal` and `library.db-shm` sit beside it, and a copy of the library takes all three files. SQLite's write-ahead log needs the database on this computer's own disks: keep `ModShelf__DatabasePath` out of network folders.
- Credentials never go in the database. They go to the system's secret store: DPAPI-encrypted files on Windows, the Keychain on macOS, and the Secret Service keyring on Linux. Without a keyring (libsecret missing, no session bus, or no Secret Service on it), Linux falls back to files encrypted for this computer, and the account commands warn about it. A keyring that's there but fails, such as one that's locked or doesn't answer, is still used: ModShelf logs that it failed, and linking or unlinking says why it couldn't. To keep test credentials apart from your own, set `ModShelf__SecretScope` to another name (the Keychain and the keyring) and `ModShelf__SecretsFolder` to another folder (the files).
- Logs go to stderr, warnings only (`Logging__LogLevel__Default=Debug` shows more there), and to a log file a day in a `logs` folder beside the library database, such as `cli-20261029.log`, from Information up. A week of files is kept. Each run's logs start with a line saying what it runs, the command and the names of the options given, never their values (such as `mcp --http --port`), its process ID and the process that started it, such as an agent's app. Slow library database commands are logged as in the desktop app, in the log files alone: standard error keeps to what a command, or an MCP server, has to say.
- A library database that can't be opened, such as a damaged one or one another program holds, is named, with what's wrong and what to do; ModShelf leaves it as it is, and every command exits 1.

## Environment variables

| Variable | What it does |
|---|---|
| `ModShelf__DatabasePath` | Uses another library database, such as while testing; `settings.json` moves with it. Keep it on this computer's own disks. |
| `ModShelf__SecretScope` | Keeps credentials apart under another name in the Keychain (macOS) or the keyring (Linux). |
| `ModShelf__SecretsFolder` | Keeps credentials in another folder where they're stored as encrypted files (Windows, or Linux without a keyring). |
| `ModShelf__Nexus__ClientId` | A Nexus Mods client ID, to try signing in through the browser. Never commit one. |
| `ModShelf__DesktopApp` | The desktop app's executable, for `links take` from a build from source. |
| `ModShelf__CommandLine` | The command line's executable, for a desktop app built from source that serves the MCP server. |
| `ModShelf__UpdateFeed` | A folder `build/pack.ps1` packed into, to try the app's self-update against releases of your own. |
| `Logging__LogLevel__Default` | How much the command line logs to standard error, such as `Debug`; warnings only by default. |
