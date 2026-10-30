# 36. The nxm:// link handler

- Status: Accepted
- Date: 2026-10-30

## Context

Nexus Mods' "mod manager download" buttons hand a download over with an `nxm://` link, which the system opens with the
app registered for the scheme. Until now ModShelf registered nothing: a free account's download waited until the user
copied the button's link and pasted it (ADRs 0014 and 0024), and a Premium account's button did nothing for ModShelf.

Vortex and Mod Organizer 2 register the same scheme, so the plan makes ModShelf's registration opt-in, and asks that the
app that had the links gets them back when ModShelf stops, or is uninstalled. The human approved the shapes below, a
link with nothing waiting for it going into the library, the window coming forward on the Downloads screen, and macOS
being wired now and checked once the installer makes an app bundle (step 4).

## Decision

- **A source says which scheme its website hands downloads over with**: a new capability, `IHandoffScheme`, in
  Sources.Abstractions. Nexus Mods' is `nxm`. A second source with a scheme of its own, such as `curseforge`, needs
  nothing more.
- **Application/Links**:
  - `ISchemeRegistration`, the port to the system's register: which app opens a scheme's links (`SchemeState`: ModShelf,
    another app with its name, none, or unavailable here), registering the desktop app, and unregistering it.
  - `ILinkHandlers` lists each source's scheme with its state, and takes or gives back a source's links.
  - `IOpenedLinks.OpenAsync` downloads a link another app handed over. An unfinished download of its file outside the
    library goes on with it: one waiting for the website, paused, or failed. Otherwise the file goes into the library
    (`ILibraryDownloads.AddAsync`), which also goes on with one waiting in the library. So a link never downloads a file
    a second time.
- **Platform/Links** registers the desktop app, and keeps what it replaced beside the registration, so that whatever
  ModShelf runs the giving back, such as the uninstaller, puts back the same app:
  - Windows: the scheme's class under the current user's `Software\Classes`, as Vortex and MO2 write it. The class it
    replaces is copied to `Software\ModShelf\Previous\nxm` first, and copied back. A `ModShelf` value marks the class as
    ModShelf's. An app the user chose in Windows' own "open with" prompt (`UserChoice`) wins over any class, and
    ModShelf can't change that choice; it names that app instead.
  - Linux and other freedesktop systems: `modshelf-nxm.desktop` in `~/.local/share/applications`, made the default for
    `x-scheme-handler/nxm` in `~/.config/mimeapps.list`. The previous user default is kept in the entry as
    `X-ModShelf-Previous`. Which app opens the links follows the specification's order of `mimeapps.list` files, so a
    desktop's own list that names another app is reported as that app. The `Exec` line quotes an argument only when it
    must, since `xdg-open` outside a desktop environment splits it at spaces and keeps quotes as they are.
  - macOS: Launch Services, from the app bundle only, which declares the scheme; the previous bundle ID is kept in
    ModShelf's preferences. A run outside a bundle, such as `dotnet run`, is unavailable.
- **What the system starts** is the desktop app, beside the CLI or where `ModShelf__DesktopApp` says, with the arguments
  that keep it on this library: `--ModShelf:DatabasePath=…`, `--ModShelf:SecretsFolder=…` and `--ModShelf:SecretScope=…`
  when they aren't the defaults. The system starts it without the user's environment, and the single instance is per
  library (ADR 0029), so a checklist's test library gets its links rather than the user's own.
- **A link reaches the app** as a command-line argument when it starts, through the single-instance pipe when it runs
  (ADR 0029), or, on macOS, as Avalonia's open-URL activation. The window comes forward on the Downloads screen, which
  says what became of the link as it does for a pasted one. Only `--key=value` arguments are configuration, so a link
  and its key never are.
- **Settings has a "Links from websites" card**: who opens each site's links, **Open them with ModShelf**, and **Give
  them back to Vortex** (or **Stop opening them**). It reads them again each time the Settings screen is shown, since
  Vortex, for one, takes them back as it starts.
- **`ms links status | take | give-back`** does the same from the CLI.
- Events 1301–1303 (taken, given back, a link opened); Presentation's 926–929. None holds a link.

## Consequences

- Registering changes only the current user's settings; no administrator rights are needed.
- On Windows, when the user once chose another app in the "open with" prompt, ModShelf can't take the links, and says
  where to choose it.
- On macOS, giving the links back when no other app had them leaves them with ModShelf: Launch Services has no way to
  say "none", and the bundle declares the scheme.
- A test library's registration points at its folder. The checklists give the links back before deleting it.
- The installers (step 4) give the links back on uninstall through the same `ILinkHandlers`.

## Testing

- Application: the handlers list, take and give back through an in-memory register, and refuse a source without a
  scheme; an opened link goes on with a download outside the library that is waiting, paused or failed, but not one that
  finished or is of another file, and otherwise goes into the library; the log never holds the link. The contract tests
  check that a source's scheme is a plain one it recognizes as a handoff.
- Platform: the Windows registration over an in-memory registry (taking from Vortex, giving it back as it was, the
  machine's class, an app that took them since, the user's choice), and on Windows over a test key of the real registry;
  the Linux registration over test folders (each `mimeapps.list`, a desktop's own list, the first app that says it opens
  them, hidden entries, giving back to the user's default or the system's); the quoting of Windows command lines and
  desktop entries; Launch Services on macOS outside a bundle.
- Presentation: the card's text per state, its buttons and notes, its failures, and reading again when shown; the
  Downloads screen's notes for an opened link; the main window opening only a launch's links.
- CLI: `links` with an in-memory register; the hosts' arguments for the desktop app.
- By hand: the checklists' new sections, and on Linux `xdg-mime` and `xdg-open` agreeing with the registration.
