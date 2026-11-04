# 66. Settings and UI state

- Status: Accepted
- Date: 2026-11-04

## Context

Each screen showed its mods with previews of its own, `previews.<screen>` in `settings.json`, whatever game it showed.
A game whose mods have poor pictures, or a game whose pictures matter most, couldn't be shown its own way.

The desktop app will also remember things for the user: where its window was, whether the menu was collapsed, the
screen last open (step 14), and Browse's recent searches (steps 10 and 11). Nobody sets those on purpose, resetting the
settings shouldn't lose them, and the CLI and the MCP server have no use for them, so they don't belong among the
settings.

The human chose (2026-11-04):

- a game's previews are used where its mods show as that game's: its page in the Library, and Browse once it's chosen;
- the previews buttons there change what you're looking at: the game's own when it has some, or else the screen's;
- the UI state goes in `ui-state.json` beside `settings.json`, in the shape below.

## Decision

The rule: **a value the user sets on purpose is a setting**, in `settings.json`. **A value the app remembers for them is
state**, in a file named for what it holds.

### A game's previews

- **Application:**
  - `GameSettings.Previews` is `Off`, `Thumbnails` or `Cards`, or null for the screens'.
  - `AppSettings.PreviewFor(screen, game)` gives the game's own on the Library and Browse, and the screen's elsewhere,
    or for a game without its own.
- **`settings.json`:** `"previews"` in a game's settings, between `"browse"` and `"hidden"`: `off`, `thumbnails` or
  `cards`, in any case. Anything else is an error naming the key, as for the other settings.
- **CLI and MCP:** `games.<site>/<game>.previews` for `settings set`, `settings reset` and `settings show`, and their
  tools. The tools' descriptions name it.
- **Presentation:**
  - `ModPictures` keeps each game's own previews beside the screens':
    - `LevelOf(screen, game)` gives what the screen shows for the game, and `OwnLevelOf(screen, game)` the game's own
      there, if it has some.
    - `SetLevelAsync(screen, game, level)` changes the game's own when it has some there, or else the screen's.
    - `SavedAsync()` waits for every choice made so far to be saved.
    - `ShowGames(settings)` takes the games' previews as the Settings screen reads them, and tells the screens only
      when they changed.
  - `PreviewsViewModel.ShowFor(game)`:
    - the Library sets the open game, and none while the games show;
    - Browse sets the chosen game.

    While the previews are the game's own, `IsGamesOwn` is true, the buttons' label says "This game's previews", and
    a tooltip says a choice changes the game's.
  - **Settings › Games › a game** has **Previews**:
    - it uses the screens' ("The screens' own: thumbnails in the Library, cards in Browse.") until **Override** gives
      the game the Library's;
    - the game's own are then **Off**, **Thumbnails** or **Cards**, and **Use default** gives the screens' back.

    They count among the game's overridden settings, and copying another site's game takes them ("previews").
  - **The Settings screen reads the settings again each time it's shown.** It waits for the previews chosen on other
    screens to be saved first. So it shows a game's previews changed on its Library page, or a change made from the
    CLI. What it reads goes to `ModPictures`, so the other screens follow a change made in Settings at once.

### UI state

- **Application, `State/`:**
  - `UiState`:
    - `Window`, a `WindowPlacement`: the corner in pixels, the size, which must be above 0, and whether it was
      maximized;
    - `MenuCollapsed`;
    - `Screen`, the screen's name, trimmed;
    - `BrowseSearches`, the newest first, each once (case aside), at most 10, none empty.

    `WithBrowseSearch(text)` puts a search first. Two states are equal when they remember the same.
  - `IUiState`:
    - `Location`;
    - `GetAsync`, which never fails;
    - `ChangeAsync(change)`, one change at a time, saved only when it changes something.

    It's registered by `AddUiState()`.
  - The port `IUiStateStore`.
- **Infrastructure:** `JsonUiStateStore`, registered by `AddUiStateFile(path)`. The desktop app keeps the state in
  `ui-state.json` beside `settings.json`:

  ```json
  {
    "window": { "x": 120, "y": 80, "width": 1280, "height": 800, "maximized": false },
    "menuCollapsed": true,
    "screen": "browse",
    "browseSearches": [ "skyui", "armour" ]
  }
  ```

  - A file that can't be read is forgotten, which is logged (event 1600), and replaced when the state is next saved.
  - A value that isn't what it should be is left out.
  - Keys it doesn't know stay.
  - It's written as `settings.json` is: to a temporary file, then put in place in one step.
- **Not exposed:** the CLI, the MCP server and diagnostics leave the state out, and resetting a setting doesn't touch
  it.
- **Nothing writes it yet:**
  - step 14 remembers the window, the menu and the screen;
  - steps 10 and 11 remember Browse's recent searches.
- **Code organization:** `State` is a feature, beside `Settings`, and the UI state file's log messages take event IDs
  1600 to 1699.

## Consequences

- The previews buttons on a game's Library page, or in Browse with it chosen, may change a game's setting rather than
  the screen's. The label says which.
- A game's page can look unlike the screen's other games.
- Step 12 moves every screen's previews into Settings and takes the buttons off the screens. The game's **Previews** row
  it plans is this one.
- Showing the Settings screen reads `settings.json` again. A problem said earlier gives way to what that reading says.
- `ui-state.json` moves with the library database, as `settings.json` does.

## Testing

- Application:
  - a game's previews on each screen, and only its own values;
  - `UiState`'s searches: the newest first, each once, trimmed, at most 10, and empty ones ignored;
  - the screen trimmed, equality, and the window's size;
  - `IUiState` reading each time, changes one at a time, nothing saved for no change, a failed save, and refusals.
- Infrastructure:
  - a game's previews written where the file's example has them, read in any case, wrong values said, and forgotten
    when gone;
  - `ui-state.json`:
    - the documented JSON, and nothing for no file;
    - keys it doesn't know kept, and a window no longer remembered taken out;
    - a broken file, or a folder in its place, forgotten, logged and replaced;
    - windows that can't be, and wrong values, left out;
    - a relative path refused, and the registration.
- CLI and MCP: setting, showing, refusing and resetting a game's previews.
- Presentation:
  - each screen with a game's own previews and without;
  - a choice changing the game's own or the screen's;
  - a failed save kept while the app runs, and `SavedAsync` waiting;
  - `ShowGames` telling the screens only of a change;
  - the Library's game page and the games;
  - Browse's chosen game;
  - the Settings row overridden, changed and set back, starting as the Library's;
  - the copy message, and showing again after a change on another screen or from the CLI, or with unreadable settings.
- 62 deliberate bugs, all caught. Two were rewritten to build: dropping a field's only assignment, or the last use of
  an instance field, fails the analyzers.
- By hand:
  - `docs/manual-checklist.md` 7.5a;
  - `docs/manual-checklist-desktop.md` 10.2, 10.2d and 10.2e;
  - `docs/manual-checklist-mcp.md` 3.1a.
