# 71. The game picker

- Status: Accepted
- Date: 2026-11-05

## Context

Browse chose its game in an auto-complete box: every game the sites list, by name, those starting with what's typed
first, with the site in brackets when two sites can search. The games the user has mods of were lost among thousands,
and Browse's searches were forgotten, though `ui-state.json` was made to keep them (ADR 0066). Settings' **Add a game…**
asked for a game's key, such as `stardewvalley`, then for its site when two sites have games.

The mockups (step 8) showed a picker under Browse's game: the chosen game as a chip, **Your games** then other games,
recent searches while nothing's typed, driven by the keyboard. The plan has the same picker serve **Add a game…**.

The human chose (2026-11-05):

- the recent searches are **the words searched in Browse**, the last 10, kept in `ui-state.json`; choosing one searches
  it again in the chosen game;
- **Add a game…** is **a dialog with the picker**: a game found by its name among the sites' games, and a key typed
  still working when the sites can't list theirs;
- **Your games** go **most mods first**, ties by name.

## Decision

### The picker

`GamePickerViewModel`, in Presentation's `ViewModels/Games/`, shared by Browse and Settings:

- **The field:** `Query`, what's typed; the chosen game as a chip (`Picked`: its cover, name and, when the games offered
  are of two sites or more, its site); the chip's × (`ClearCommand`) takes it away to choose another, and closing the
  list without choosing puts it back.
- **The list** (`IsOpen`), as `Entries`, each group's first with its heading:
  - **Your games:** the games offered that the library has mods of, the one with the most first, ties by name, each
    saying "21 mods in your library" and showing its cover;
  - once something's typed, the other games whose names have it (`NameQuery`, step 4), those starting with it first,
    50 at most, under "Other games on Nexus Mods" ("Games on …" when the library has none of the games offered), each
    "Not in your library" and its initial on its colour, so typing doesn't read 50 pictures; "120 more: type more of
    the name." says what's left out;
  - with nothing typed, "Type to find any of the 3,412 games on Nexus Mods.", and the recent searches (`Recent`), each
    searching again; with nothing found, "No game's name has “zzz”."
- **The keys,** from the field: ↓ and ↑ move the highlight (`Highlighted`), opening the list first; Enter chooses the
  game highlighted, or, where the picker takes typed text, hands that on; Escape closes the list and forgets what was
  typed. Typing or focusing the field opens it. The game highlighted stays while it's listed.
- Games are offered as the owner reads them (`ShowGames`), with the library's counts (`ShowLibrary`), the recent
  searches (`ShowRecent`) and the game chosen elsewhere (`ShowPicked`).

### Browse

- `Picker` replaces `GameText` and `GameMatches`; `Games` and `Game` stay. The picker offers the games but the hidden
  ones, follows the library's changes for its counts, and shows Browse's game as the chip. Choosing one sets `Game`.
- A search by words is remembered as Browse's newest (`UiState.WithBrowseSearch`, through `IUiState`): a link or blank
  words aren't. The recent searches are read as Browse starts. A search that can't be remembered is logged (event 951)
  and still searched.
- Browse gains `IUiState`; the desktop app already registers it.

### Add a game…

- `AddGameViewModel`, in Settings: the picker over the games of every site that can list them (`IModLookup`, which
  keeps each site's list for the run), but those listed already; no recent searches.
- Choosing a game adds it at once. Enter or **Add** with no game listed adds what's typed as a game's key, as before:
  `nexus/stardewvalley`, or a key a listed game has, or the one site's, or the site the user picks.
- A site that can't list its games says so, "Nexus Mods couldn't list its games: …", then "Type a game's key to add it,
  such as skyrimspecialedition."; a failure that isn't the site's is logged (event 952) and said as "Something went
  wrong."
- `IDialogService.AddGameAsync` shows it; `SettingsViewModel` gains `IModLookup`.

### Desktop

- `Views/Games/GamePicker`: a `Border.pickerField` with the chip and a bare `TextBox` whose keys drive the list, and a
  `Popup` under it, as wide as the field, drawn in the window's overlay (so its corners and shadow show on every system)
  and closing when clicked outside. `Views/Games/GamePickerList` is the list; the game highlighted scrolls into view.
- `Views/Dialogs/AddGameDialog`, closing when the view model asks, with the list in place under the field
  (`GamePicker.IsInline`): a popup can't reach past the small dialog.
- Browse's top row gives the picker two thirds of its width, beside **Tracked mods**.

## Consequences

- `ui-state.json` is now written, for Browse's searches; the README says so.
- Before the sites list their games, as without an account, the picker offers nothing to choose, as the box did, and says
  nothing of what's typed: in Add a game…, that's a key.
- The game box's tests became the picker's: with nothing typed it lists your games, not every game.
- With two sites in use but one that can search, the picker shows no sites: the games it offers are of one site.

## Testing

- The picker: your games by most mods, ties by name, with their counts; typing, your games then the others with sites
  and headings; the heading without a library; nothing found; 50 at most and how many more; the arrow keys, Enter and
  clicking; the arrows opening the list; the highlight kept while listed; typed text handed on; Escape; the chip's ×;
  a game the sites don't list; covers for your games and the chosen one only; recent searches shown, hidden while
  typing, searching again; none without a search to run.
- Browse: your games following the library; the chip; choosing a game; hidden games; recent searches read at start,
  remembered newest first, case aside; links and blanks not remembered; a recent search searching again; one that can't
  be remembered logged; two sites' games with their sites.
- Add a game…: the sites' games not listed, and how many; a game chosen added and shown; a key typed; a site that can't
  list its games; an unexpected failure logged; closing.
- `BrowseGameBoxTests` now ask the picker, and `SettingsViewModelTests.AGamesOwnPatternIsAddedEditedAndRemoved` checks
  the new dialog rather than the key question; the other tests adding a game by key type it in the picker.
- 57 deliberate bugs, all caught. One got through at first, the highlight going back to the first game as typing
  narrowed the list, since the game highlighted was the first anyway: the test now highlights another. Two didn't build,
  for the nullable checks and a missing name, and were rewritten.
- Looked at here, on Linux without an account (so no games are listed): the field and its list drawn in the window,
  and Add a game… with the list in place and the site's problem. Typing in the dialog lost its first key here until
  the field was clicked; that looked like the virtual screen's window manager, not ModShelf, and wants a look on
  Windows (10.2g).
- By hand: `docs/manual-checklist-desktop.md` 3.1, 3.1a, 9.2c, 10.2 and 10.2g.
