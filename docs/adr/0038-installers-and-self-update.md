# 38. Installers and self-update

- Status: Accepted
- Date: 2026-10-30

## Context

Phase 6 ships ModShelf to people who don't build it. The plan's step 4 asks for:

- an installer per system: a per-user Setup.exe on Windows, with a Start-menu shortcut that shares its app ID with the
  toasts, so that a click on a notice opens Updates; an `.app` in a `.pkg` on macOS, declaring the `nxm` scheme, whose
  Dock icon brings the window back; an AppImage on Linux;
- uninstalling that puts the `nxm://` handler back (ADR 0036);
- updates from the GitHub releases, looked for at start and from Settings, and applied on restart after asking.

The human approved Velopack 1.2.161 for all of it when Phase 6 started. For this step they approved the shapes as
listed, a click on a Windows notice going through a `modshelf://` link, and pre-releases while ModShelf is 0.x. They
chose that the look at start only says a newer ModShelf is there, and that the user downloads it, over the recommended
option.

## Decision

### Packing

- **`build/pack.ps1` and `build/pack.sh`** publish the desktop app self-contained for each runtime and pack it with
  `vpk`, a local tool in `.config/dotnet-tools.json`, into `artifacts/releases`:
  - `win-x64`: a per-user Setup.exe with a Start-menu shortcut carrying the app ID `ModShelf.App`, and the portable zip
    vpk makes beside it;
  - `linux-x64`: an AppImage. vpk needs `mksquashfs` for it, and `zstd` for a delta package;
  - `osx-arm64` and `osx-x64`: an `.app` in a `.pkg`, packed on a Mac only. vpk takes `build/macos/Info.plist` as it is,
    so the template says everything the bundle needs, with the version put in by the scripts: the bundle ID
    `io.github.valjean15.modshelf`, the icon, and the `nxm` scheme for Launch Services (ADR 0036).
- **The pack ID is `ModShelf.App`, never `ModShelf`.** Velopack installs the Windows app into
  `%LocalAppData%\<pack ID>`, and its uninstaller deletes that whole folder. `%LocalAppData%\ModShelf` holds the library
  database, the settings, the logs and the secrets, which an uninstall must leave. `PackagingTests` fails the build if
  either script gives `ModShelf`.
- **Each runtime is a channel of its own, named after it**, such as `releases.win-x64.json`: an installed ModShelf
  looks for updates in the channel it was packed in, so the two macOS builds never cross.
- **The version comes from the git tag** through MinVer (ADR 0035), or from `-Version`/`--version`, which goes to the
  publish as `MinVerVersionOverride`, so the app says the version its package has.
- **Nothing is signed yet.** Step 6 adds signing once the secrets exist; until then Windows SmartScreen and macOS
  Gatekeeper warn on first start.
- **The icons come from `modshelf.svg`**: `build/render-icons.py` renders `modshelf.ico`, which the Windows executable
  carries (`ApplicationIcon`), and `build/macos/modshelf.icns`.

### In the app

- **Velopack's start-up code runs first in `Main`**, before the host. The installer, an update and the uninstaller
  start the app with Velopack's own arguments; it handles those and ends the process. It sets the process's app ID to
  `ModShelf.App`. Applying a downloaded update at start is turned off: a second launch, such as a link's, would apply
  it under the app running. Only the user's restart applies it.
- **Application/Releases** has the ports:
  - `IAppReleases`: whether this ModShelf is installed, the release downloaded and waiting for a restart, looking for a
    newer `AppRelease` (its version and Markdown notes), downloading it with its progress, and restarting into it.
    Platform's `VelopackAppReleases` asks the releases of `github.com/Valjean15/ModShelf`, pre-releases too while
    ModShelf is 0.x, and checks each package's SHA-256. `ModShelf__UpdateFeed` points it at a folder or a web address
    with Velopack's feed instead, for testing. A build from source, the CLI and the tests aren't installed.
  - `IAppUninstall` gives back each site's links where ModShelf opens them, or is registered behind an app the user
    chose, through `ILinkHandlers`, then removes what ModShelf registered for itself through the port
    `IAppRegistrations`: on Windows, the toasts' app ID and the `modshelf` scheme. A part that fails is logged and the
    rest still runs. These two were added while building, beyond the listed shapes, so that the uninstaller's work is
    tested outside the desktop app, which has no test project.
- **The Windows uninstaller** runs `IAppUninstall` from Velopack's uninstall hook, which gives it 30 seconds. It starts
  the app without arguments, so it's ModShelf on the default library.
- **A new setting**, `app.checkForUpdates` (`AppSettings.CheckForAppUpdates`), true unless set, says whether the
  installed app looks for a newer ModShelf when it starts. Settings that can't be read leave the default.
- **The About card** shows where ModShelf's updates stand (`AppUpdateViewModel`): **Check for updates**; a newer
  release with its notes as plain text and **Download**; its progress; then "ModShelf 0.9.1 is downloaded. Restart
  ModShelf to update to it…" and **Restart to update**, which starts Velopack's updater and quits through Presentation's
  new `IAppExit`. The updater waits for ModShelf to end, puts the new release in place and starts it. The look at start
  only says a newer release is there, and the Settings screen's badge shows it until the restart; a look at start that
  fails is only logged. A build from source says updates come with the installed ModShelf, without the buttons. The
  card's check box changes `app.checkForUpdates`.
- **A click on a Windows notice opens the Updates screen** through ModShelf's own scheme: the toast is protocol-activated
  with `modshelf://updates`. Before a toast shows, ModShelf registers itself for `modshelf://` links as it does for
  `nxm://` ones (ADR 0036), on this library, with no question, since no other app opens them; when it can't, the toast
  opens nothing. The system starts the desktop app with the link, or hands it to the one running, as with an `nxm://`
  link, and the main window opens the screen the link names. A protocol activation needs neither the installer's
  shortcut nor a COM activator, so a build from source's notices open Updates too.
- **On Linux, the system starts the AppImage itself for a link**, from the `APPIMAGE` variable its runtime sets, since
  the folder it runs from is mounted afresh at each start. Only when this app's folder is inside `APPDIR`, so that
  another AppImage's variables, such as a terminal's, are never taken for ModShelf's.
- **On macOS, a click on the Dock icon** shows the main window again, as the tray's **Open ModShelf** does.
- `.gitignore`'s rule for build results, `[Rr]eleases/`, no longer hides the `Releases` feature folders.
- Events: Platform's 803 (a notice's click can't open ModShelf), Presentation's 935–940, and 1304–1306 (the uninstall's
  failures).

## Consequences

- An installed ModShelf updates itself after asking; a build from source never does.
- A downloaded release waits until the user restarts into it, through one start or many.
- Uninstalling on Windows gives the links back that ModShelf on the default library took. A test library's
  registration shows as another ModShelf's and stays; the checklists give it back themselves.
- macOS and Linux have no uninstaller: deleting the app leaves its registrations. Linux keeps
  `modshelf-nxm.desktop` pointing at an AppImage that's gone, so **Stop opening them** on the Settings screen comes
  first; macOS forgets a deleted bundle by itself.
- Velopack downloads through its own `HttpClient`, outside ModShelf's `IHttpClientFactory` and resilience pipeline. The
  releases are public, few and large, each checked by its SHA-256, and a failed download is tried again from the card. A
  downloader on `IHttpClientFactory` can replace it if that ever matters.
- Velopack's updater keeps its own log, `velopack_ModShelf.App.log` in the temporary folder: the packages and folders
  it works on, and at most the addresses of the public release files, which carry no keys.
- A delta package needs the previous release's full package in the output folder. The release workflow (step 5)
  downloads it first, and uploads each channel to the GitHub release.

## Testing

- Platform: `VelopackAppReleases` over a feed in a folder, as an installed 0.9.0 with Velopack's test locator: a newer
  release with its notes, the same one, a download checked whole, a damaged package refused, only a found release
  downloaded or applied, a feed that can't be read, and nothing installed without Velopack's start-up code. The
  sources for GitHub, a web address and a folder. Notice clicks registering `modshelf`, taking it from another
  ModShelf, none without the desktop app, and failures; the toast's launch attributes; the uninstall's removals over an
  in-memory registry; the AppImage's path, and another AppImage's variables refused. The registrations.
- Application: the uninstall gives back the links ModShelf opens or is behind, leaves the others, goes on past a
  failure, and stops when cancelled. Settings: `app.checkForUpdates` kept, refused when it isn't true or false, set and
  reset, here and in the CLI.
- Presentation: the update part's states, the look at start only telling, its quiet failure, a release downloaded
  before, the download's progress on the UI thread, failures, restarting and quitting, and the notes as text; the
  Settings screen's start look, badge and check box; `modshelf://` links opening the screen they name.
- Architecture: neither pack script gives the pack ID `ModShelf`.
- Live on Linux: 0.9.0 and 0.9.1 AppImages packed with `build/pack.sh` into a folder, 0.9.0 run with
  `ModShelf__UpdateFeed` on Xvfb. The badge and "ModShelf 0.9.1 is available." at start, **Download**, a quit and a
  start that kept 0.9.0 with the download waiting, **Restart to update**, and 0.9.1 running from the same AppImage path,
  with "This is the newest ModShelf." Taking the `nxm` links wrote the AppImage's path in `Exec`, a link through
  `xdg-open` reached the running app, and giving them back removed the entry. The About card of a build from source.
- By hand, on each system: [the installer checklist](../manual-checklist-installer.md).
