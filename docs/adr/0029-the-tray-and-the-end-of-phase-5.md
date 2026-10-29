# 29. The tray, and the end of Phase 5

- Status: Accepted
- Date: 2026-10-29

## Context

Step 6 closes Phase 5 (ADR 0023). The plan gives it a tray icon that keeps the background update checks running with the window closed; the human chose, when the phase was planned, that the first close asks whether to keep running in the tray and remembers the answer. Phase 5 is done when every flow of Phases 1–4 works from the app without the CLI, and ViewModel tests cover each screen's commands.

Until now the app ended when its window closed. Three things stood in the way of a tray:

- **A second launch.** With the app in the tray, starting it again, such as from its shortcut, would open a second app. That one can't run the downloads (ADR 0027), and would check for updates again.
- **Linux trays.** Avalonia's tray icons on Linux need a tray host on the session bus (`org.kde.StatusNotifierWatcher`). KDE and most desktops run one; stock GNOME doesn't, without the AppIndicator extension. Avalonia doesn't say whether its icon showed, so a window closed "to the tray" there would leave the app running with no way back to it.
- **An icon.** The app had none, and a tray icon needs one.

The human chose, before anything was built:

- **The tray as proposed:** a `window.closeToTray` setting (ask, keep running, quit), a three-way question at the first close, a menu with Open ModShelf, Check for updates now and Quit ModShelf, a tooltip counting the updates, and an icon drawn now.
- **Linux: detect the tray host,** through GIO as the keyring code does, with no new package; without one, closing quits.
- **Single instance now,** over a named pipe, which Phase 6 reuses to hand `nxm://` links to the running app.
- **The review:** an audit of every flow and command, plus a checklist for a live run with both accounts.

## Decision

### Closing the window

- **`AppSettings.CloseToTray`:** true keeps ModShelf running in the tray when the window closes, false quits it, and null asks the first time. **`ISettingsService.SetCloseToTrayAsync`** sets it; `settings.json` keeps it as `"window": { "closeToTray": true }`, and the CLI's settings commands know `window.closeToTray`.
- **`TrayViewModel.DecideCloseAsync`** (Presentation) decides:
  - with no tray to run in, it quits;
  - with a remembered answer, it follows it;
  - otherwise it asks "Keep ModShelf running?" with **Keep running in the tray** and **Quit ModShelf**, and remembers the answer. Closing the question keeps the window open. Settings that can't be read mean asking, and an answer that can't be kept still counts this time (event 923).
- **`IDialogService.ChooseAsync`** asks with a button per choice, the first the main one, and returns the choice or null. Desktop shows it as a `ChoiceDialog`.
- **Desktop's `AppTray`** carries it out: it keeps the window open while the question is asked, then hides it or quits. Closing because the app or the system is ending goes ahead. The app now runs until it's told to quit (`ShutdownMode.OnExplicitShutdown`), so the downloads and the update checks go on with the window hidden.
- **The Settings screen** offers it under "Closing the window": ask the next time, keep running in the tray, or quit. Without tray icons, the choice is disabled and the screen says closing quits.

### The tray

- **The icon** opens the window when clicked. Its menu: **Open ModShelf**; **Check for updates now**, which runs the Updates screen's check and says what it found in a notice, since the window may be closed; and **Quit ModShelf**, which takes the icon out of the tray at once and ends the app.
- **Its tooltip** is "ModShelf", or "ModShelf: 3 updates" while updates are found. On Linux, Avalonia gives it to the tray as the icon's title.
- **The icon itself** is three books on a shelf, white on blue: `Assets/modshelf.svg`, rendered at 256 pixels for the windows' title bars and the taskbar, and at 64 for the tray. Phase 6 makes the installers' icons from it.

### Whether there's a tray

- **`ITrayHost.IsAvailableAsync`** (Application, `Desktop/Ports`) says whether an icon put in the tray now would show.
- **Platform's `SystemTrayHost`:** always on Windows and macOS. On Linux, while something owns `org.kde.StatusNotifierWatcher` on the session bus, asked through GIO's `NameHasOwner` each time, since a tray host can start or stop, and off the caller's thread. No bus, or no GIO, means none, and is logged (events 1210–1211).
- The window's close and the Settings screen ask it.

### One app at a time

- **`ISingleInstance`** (Application, `Desktop/Ports`): `TryClaim(arguments)` makes this process the desktop app, or hands the arguments to the one running and returns false; `LaunchedAgain` tells the running app of each launch.
- **Platform's `PipeSingleInstance`:**
  - It holds `desktop.lock` in the library database's folder, with no sharing, as the download queue's lock does (ADR 0027): the system lets go when the process ends. So each library has its own app, and a test database its own.
  - It listens on a named pipe named after the folder's path, for this user only. A second launch sends its arguments as a line of JSON and waits up to 5 seconds for "ok".
  - A pipe stays waiting while each launch is answered: on Linux and macOS the pipe is a socket that closes with its last server, which cut off a launch that came in between.
  - Arguments are never logged; from Phase 6 they can hold `nxm://` links. Events 1200–1203, in a new block, 1200–1299, for the desktop app's single instance and tray.
- **The desktop app** claims it before anything starts. A second launch brings the running app's window back, restored if minimized, and exits 0. One the running app doesn't answer, such as a hung app, says so in a window and exits 1 (events 1001–1002).

### The end-to-end review

Every flow the CLI has from Phases 1–4 works from the app:

| From the CLI | From the app |
|---|---|
| `download <link>` | Downloads: **Download**; a file's **Download** in Browse; an update's **Download** |
| `download <link> --to <folder> [--name <file>]` | **Save to a folder…** on the Downloads screen or a file in Browse, which asks for a name when the link gives none |
| `download nxm://…`, finishing a handoff | A waiting download's **Paste link…**, or the link in the Downloads screen's box |
| Ctrl+C, then the same command again | **Pause** and **Resume**; quitting the app and starting it again |
| `queue list` | The Downloads screen, with **Clear finished** |
| `mod show <link> [--all] [--changelog]` | A link in Browse's search box; **Show more files**; **Changelog** |
| `mod search …` | Browse: the game, words, author, adult content, nine orders, **Load more** |
| `mod updates --game <game>` | Browse's **Recently updated** order (ADR 0026) |
| `mod tracked`, `mod track`, `mod untrack` | **Tracked mods** for the chosen game; **Track on Nexus Mods** on a mod |
| `library list`, `library watch`, `library unwatch` | The Library screen, with **Watch** and **Unwatch**; **Watch** on a mod in Browse |
| `library check`, `library updates [--changelog]` | Updates: **Check now**, the updates with **Changes**; the tray's **Check for updates now**; the background checks |
| `library move <folder>` | Settings: **Choose another folder…** and **Use the default folder** |
| `settings show`, `set`, `reset` | The Settings screen, every setting |
| `account list`, `link`, `check`, `unlink` | The Accounts screen |

The differences are on purpose: Browse pages by 20 mods with **Load more** rather than taking a page size; its tracked mods are the chosen game's rather than every game's; and a game's update feed is an order of the search rather than a screen of its own. The app adds deleting a library file and opening a mod's folder or page, which the CLI doesn't have.

Each of the screens' 43 commands, and each card's link buttons, is run by ViewModel tests. `docs/manual-checklist-desktop.md` takes a person through every flow with a Premium and a free account, as `docs/manual-checklist.md` does for the CLI.

### Testing

- **Infrastructure, Application, CLI:** the setting, as for the others.
- **Platform:**
  - The single instance in one process as from two: hand-overs in turn, another library's app beside it, a new app once the first ends, and once it ends while it shows itself (it finishes first, so the next can listen), an app that holds the lock but doesn't answer, one that hears but never answers, one that hangs up, a handler that fails, messages it can't read or too long, and arguments never logged.
  - The tray check's decisions over a faked bus; GIO's `NameHasOwner` on a real session bus, and its error without one.
- **Presentation:** each close decision, a remembered and a refused answer, unreadable settings; the tooltip; checking from the tray, with and without watched mods and while a check runs; the Settings screen's choice and whether there's a tray.
- **By hand, on a virtual display:**
  - two launches on one library (the second exits 0 in about half a second, and the first window comes back from minimized), and a frozen app (the window saying it didn't answer);
  - under a private session bus with a stand-in tray host written for the check (`scratchpad/sni`, Python's `dbus_next`), which registered the icon and drove its menu: the first close asking, keeping running in the tray (window hidden, `"closeToTray": true` saved), the icon bringing the window back, the next close not asking, **Check for updates now**, and **Quit ModShelf** ending the app with exit code 0.

## Consequences

- **ModShelf can run with no window.** Quitting is in the tray's menu, the close question, or the setting.
- **A second launch of the desktop app never opens a second window** for the same library. Libraries in different folders get an app each.
- **On GNOME without the AppIndicator extension,** closing the window quits, as before.
- **A frozen app** keeps the next launch out until it's ended; the launch says so.

## Open

- **macOS:** clicking the Dock icon while the window is hidden doesn't bring it back yet; the tray icon in the menu bar does. The icon isn't a template image, so the menu bar shows it in colour.
- **Notices** still don't bring the window forward when clicked (Phase 6, ADR 0025).
- **`nxm://` links** handed to a second launch reach the running app but aren't acted on until Phase 6 registers ModShelf for them.
