# 74. Remember the window

- Status: Accepted
- Date: 2026-11-06

## Context

The main window opened at 1024×640, centred, with the sidebar open and the Library shown, every time. Step 5 (ADR 0066)
made `ui-state.json` and `UiState` for what the app keeps for itself: the window's place (`WindowPlacement`: its corner
in the screens' pixels, its size in the app's units, maximized or not), whether the menu is collapsed, and the screen
last open. Nothing used them but Browse's recent searches.

The plan (step 14): restore the size, the position, maximized, the menu and the screen last open; and keep the window
on a screen that exists, so one last seen on a monitor that's gone, or a smaller one, comes back fully visible. No
choice was left open.

## Decision

### Remembering

- `MainWindowViewModel` takes `IUiState` and remembers, as they change: the sidebar (`MenuCollapsed`) and the screen
  shown (`Screen`, its name). The view tells it of the window's place (`RememberWindow`). Changes go one after another
  (`Remembering`, the last), off the UI thread; one that can't be saved is logged (event 954) and the next still goes.
- The first screen, before anything's restored, isn't remembered: it isn't the user's choice.
- `MainWindow` keeps its place while it's neither maximized nor minimized, which is where a maximized window goes back
  to, and tells the view model once the window has been still for 0.6 seconds, as it's hidden in the tray, and as it
  closes. Minimizing keeps the place before.

### Restoring

- `Program` reads `ui-state.json` before Avalonia starts, as it must stay synchronous for Avalonia's STA thread, with no
  SynchronizationContext yet to block (reading it never fails). The window then opens where it was, rather than moving
  once shown.
- `MainWindowViewModel.Restore` shows the sidebar and the screen as remembered, without remembering them again; a
  screen it doesn't know, as from a later version, is passed over. Links the app starts with still open their screen
  after it.
- `MainWindow.Place` puts the window where `WindowFit.Fit` says, before it's shown:
  - on the screen the window overlaps most, moved in from the edges and no larger than its working area;
  - centred on the main screen when it overlaps none, as when its monitor is gone;
  - no smaller than the window's least size, unless the screen is;
  - the frame, the title bar and borders, which aren't known before the window shows, allowed for as 16 by 40 units, so
    a window that fills its screen keeps its title bar on it.
- `ScreenArea` is a screen's working area for the fitting, which stays in Presentation, without Avalonia, to be tested.
- With nothing remembered, the window opens as before.

### As the app ends

- Quitting from the tray, or the system ending, closes the window, which tells its place; `Program` gives that last
  change up to two seconds to be saved once Avalonia has stopped.

## Consequences

- No settings, schema or public Application change: `UiState` already held these.
- The window's place is saved often while it's moved, at most once each 0.6 seconds of stillness.
- `ui-state.json` stays apart from the settings, so resetting them keeps the window.

## Testing

- The fitting: a window that fits stays; on the second screen in its scaling; with its screen gone, centred on the main
  one, wherever the main one is listed; moved in from an edge; made to fit a smaller screen; sizes in a scaled screen's
  units; the frame kept on the screen; a screen smaller than the least window; a window smaller than the least; the
  screen it overlaps most, the overlap taken in each screen's scaling; nothing remembered, no screen, or one of no size;
  no main screen.
- The view model: nothing remembered by a new window; the sidebar and screen restored without being remembered again,
  at once even while the UI thread is busy; a screen it doesn't know passed over; the sidebar and screen remembered as
  they change, from the sidebar and from navigation; the window's place as told; changes saved one after another; a
  change that can't be saved logged, and the next one saved.
- 27 deliberate bugs, all caught. Four got through at first, and tests now catch them: the first screen listed taken for
  the main one, the overlap measured without the screen's scaling, the screen restored only once the UI thread came to
  it, and a change saved without waiting for those before it.
- Looked at here, on Linux: restored at 900×600 with the sidebar collapsed on Downloads; moving and resizing remembered,
  then the sidebar and Browse; a window remembered off every screen and too large, opening whole on the screen; a
  maximized one opening maximized.
- By hand: `docs/manual-checklist-desktop.md` 1.7.
