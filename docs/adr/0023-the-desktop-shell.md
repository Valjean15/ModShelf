# 23. The desktop shell

- Status: Accepted
- Date: 2026-10-27

## Context

Phase 5 puts a desktop app over everything Phases 1–4 built. The plan says:

- **Six screens:** Library, Updates, Browse, Downloads, Accounts and Settings.
- **Compiled bindings everywhere,** so binding errors fail the build.
- **`INavigationService` and `IDialogService` in Presentation,** with Avalonia implementations in Desktop.
- **The Fluent theme,** light or dark as the system is.
- Virtualized lists, a tray icon that keeps background checks running, and every string in `.resx` resources.
- **Done when** every flow from Phases 1–4 works from the UI with no CLI, and ViewModel tests cover each screen's commands.

Until now, the desktop app opened an empty window.

### The steps

The human chose to build Phase 5 in six steps, each delivered and tested like Phase 4's:

1. **The shell.** This ADR.
2. **Downloads:**
   - adding a link, into the library or a folder;
   - the queue, with live progress, and pausing, resuming, retrying and cancelling;
   - handoffs, by opening the page on Nexus Mods and pasting its `nxm://` link, until Phase 6 registers the handler.
3. **Library and Updates:**
   - mods by game, opening their folder, and removing files after asking;
   - watching and unwatching, and checking now;
   - updates with their changelogs, and downloading one;
   - clicking a notice opens Updates; this moved to Phase 6 (ADR 0025).
4. **Browse:** searching a game, a mod's files and changelog, and downloading or watching.
5. **Accounts and Settings:**
   - linking with an API key or the browser, unlinking, and showing the tier and the API quota;
   - every setting, including moving the library and the theme.
6. **The tray.** The first time the window closes, ModShelf asks whether to keep running in the tray and remembers the answer as a setting. This step also holds the phase's end-to-end review.

The public shapes of each step are asked for before it's built.

### Choices for the whole phase

- **Navigation:** a sidebar on the left. It can collapse to icons, and has badges on Downloads and Updates when something needs the user.
- **Icons:** simple vector paths drawn for ModShelf, rather than an icon package.

## Decision

### Presentation

**`Presentation/Services`**, where code organization puts navigation and dialog interfaces, holds what the ViewModels need from the views.

- **`Screen`** lists the six screens in the sidebar's order.
- **`INavigationService`** says which screen is shown (`Current`), and goes to another (`NavigateTo`). It raises `Navigated` when the screen changes.
  - **It's plain state, so `NavigationService` implements it in Presentation,** and the views follow it. The plan put this implementation in Desktop, but nothing in it needs Avalonia, and here its tests need no fake.
  - It starts on the Library. Going to the screen shown already does nothing.
- **`IDialogService`** shows a message (`ShowMessageAsync`), or asks to confirm with a button naming the action (`ConfirmAsync`). Desktop implements it. Later steps add text input and folder picking.
- **`IUiThread.Post`** runs what arrives from elsewhere, such as a background check or a notice, on the UI thread.
  - Desktop implements it with Avalonia's dispatcher, running at once when already on the UI thread.
  - ViewModel tests use their own, which can hold the actions back to show that nothing changes before the UI thread runs them.

**`ScreenViewModel`** is a screen as the sidebar lists it.

- It has its `Screen` and `Title`.
- **`Badge` counts what on the screen wants the user.** At 0, `HasBadge` hides it.
- The six screens are empty until their steps.

**`MainWindowViewModel`** holds the main window.

- It lists one of each screen, in order, and refuses any other set.
- **It shows the screen navigation says,** changing it through `IUiThread`. Selecting a screen in the sidebar goes to it, and a sidebar with nothing selected keeps the screen.
- `ToggleSidebar` shows or hides the screens' names.

**`AddPresentation()`** registers navigation, the six screens and the main window. Each is created once for the app's lifetime, so a screen keeps its state while another is shown.

**Presentation's `Strings` is public,** so Desktop's views show its text through `x:Static` rather than hard-coding it.

### Desktop

**`DesktopHost`** registers everything the CLI does, through the same `Add…()` methods.

- It reads the same configuration keys: `ModShelf__DatabasePath`, `ModShelf__SecretsFolder`, `ModShelf__SecretScope` and `ModShelf__Nexus__ClientId`.
- **While the app is open,** queued jobs download, finished ones are recorded into the library, and the watched mods are checked for updates, with notices (ADR 0022). That's `AddBackgroundDownloads()`, `AddLibraryRecording()` and `AddBackgroundUpdateChecks()`.
- It adds Presentation, `AvaloniaUiThread` and `AvaloniaDialogService`.
- **The container validates every registration when the app starts,** so a missing dependency fails at once rather than when its screen first opens.
- `appsettings.json` keeps the libraries' logs to warnings.

**The main window** is a `SplitView`.

- The pane holds a button that hides or shows the names, and a list of the screens. Each shows its icon, its name, and its badge on the icon, so the badge still shows when only icons do.
- The content shows the current screen.

**`App.axaml` maps each screen's ViewModel to its view by a listed data template**, not by looking views up by name, so every binding stays compiled. `AvaloniaUseCompiledBindingsByDefault` is on for the project.

**The look:**

- `Styles/Icons.axaml` holds the icons: books on a shelf, an arrow going round, a magnifying glass, an arrow down onto a line, a head and shoulders, two sliders, and the menu's three lines.
- `Styles/Shell.axaml` styles the sidebar, the badge and the screens' headings.
- The Fluent theme follows the system's light or dark theme.

**`AvaloniaDialogService`** shows a `MessageDialog`, centred over the main window, on the UI thread whichever thread asks.

- The confirming button is the default and the other cancels, so Enter and Escape work.
- Closing the dialog any other way turns it down.
- Cancelling the token closes it.
- With no main window open, such as from the tray later, it shows on its own.

### Testing

- **Presentation tests** check:
  - navigation;
  - the main window's list and order of screens;
  - showing the screen navigation says, only on the UI thread;
  - selecting, an empty selection, and hiding the names;
  - badges;
  - the registrations.
- **The app itself was run on a virtual display (Xvfb)**, with screenshots. It showed the sidebar and its icons, moved between screens, collapsed to icons, confirmed and showed dialogs, and exited when its window closed.
- **There's no Desktop test project yet.** A missing registration fails when the app starts.

## Consequences

- **The next steps fill the screens in.** Each adds what its ViewModel needs to Presentation, its view to Desktop, and its ViewModel tests.
- **A badge's count is up to its screen.** Downloads (step 2) and Updates (step 3) set theirs.
- **The CLI and the desktop app list the same registrations.** A new service is added to both composition roots.
- **The desktop app logs to the console and the debugger only.** The plan's rolling log file is still to come.
