# 75. Quick search

- Status: Accepted
- Date: 2026-11-06

## Context

With hundreds of mods, finding one meant opening its game in the Library and filtering. The plan (step 15): **Ctrl+K**,
or a search field at the top of the menu, opens one box over any screen, finding the library's mods (opening their
page), games (opening them in the Library or Browse), settings (opening the right category) and the screens; with step
4's name matching, recent searches, and the keyboard: ↑↓, Enter, Esc. The approved mockups (step 8) put the field at the
top of the menu.

The human chose (2026-11-06):

- the recent searches are kept in **`ui-state.json`**, with Browse's, as state the app remembers rather than a setting;
- a game opens **in the Library when the library has its mods**, and in Browse otherwise.

## Decision

### What it finds

- `QuickSearchViewModel`, in Presentation's `ViewModels/QuickSearch/`, a singleton the main window shows
  (`MainWindowViewModel.QuickSearch`, a new constructor parameter). Opening it reads the library and the sites' games
  afresh; a site that can't list its games (no account, offline) is passed over silently, and the library's games go by
  their keys, as on the Library screen. Anything else that fails is logged (event 955).
- With nothing typed: the recent searches, then the screens.
- With something typed, matched as `NameQuery` matches names (each word starting a word, in any order, accents and
  punctuation aside, Roman numerals as numbers), those whose names start with it first:
  - **Games**, at most 6: the library's, the one with most mods first, "In your library · 73 mods", opening in the
    Library; then the sites' others, "On Nexus Mods", opening in Browse. The row highlighted says which.
  - **Mods in your library**, at most 8, with their game, and "update found" or "pinned".
  - **Screens** whose names match.
  - **Settings**, at most 6: each part of the Settings screen whose words match, "Library › Library folder", opening
    its category; and the library's games by name, "Games › Skyrim Special Edition", opening their own settings.
- Hidden games, and their mods, are left out, as everywhere else.
- Games show their cover, as the Library does while it shows pictures, or their initial on their colour; mods their
  game's colour.

### Opening

- `IScreenOpener` (Presentation's `Services`), like `IGameSettingsOpener`: the screen is told, then shown. The Library
  opens a mod's page with its game's mods behind it, or a game's mods, out of a page or the recent activity; Browse
  chooses the game, once its games are read when it's shown for the first time; Settings ends a search and shows the
  category.
- `ISettingsIndex`: the Settings screen lists its parts (`SettingsSearchEntry`: category, its name, heading, words) from
  the sections its own search uses, so the two find the same.

### Remembering

- A search that opened something is remembered, the newest first, 10 at most, each once whatever its case
  (`UiState.QuickSearches`, `"quickSearches"` in `ui-state.json`); one that can't be saved is logged (event 956). A
  recent search's row searches it again.
- They're read the first time the box opens and kept from then on, as only quick search changes them, so a search shows
  among them at once, before it's saved.

### The keyboard

- ↓ and ↑ move the highlight, which stays on its row while the row is still found; Enter opens it; Escape, or a click
  outside the box, closes it, forgetting what was typed. The keyboard goes back where it was.

### Desktop

- `MainWindow`: Ctrl+K and Meta+K (⌘K); under ModShelf's name, a field (`Button.quickSearchOpen`) with its keys in a
  cap; collapsed, its icon alone, the keys in its tooltip. Over the window, `QuickSearchBox`: the screen dimmed, the box
  near the top, at most 640 wide, with the field, the rows under their headings, and the keys at its foot.
- `RecentIcon` (a clock) for recent searches; the settings' and the screens' own icons for theirs.

## Consequences

- No database or settings change; `UiState` gains `QuickSearches` and `MaxQuickSearches`, a public addition the human
  approved with the choice of `ui-state.json`.
- The Library, Browse and Settings take `IScreenOpener`.
- Quick search asks the sites nothing but their games, which are cached; it searches the library's mods, not the
  sites'. Browse searches the sites.

## Testing

- Quick search: the recent searches and screens with nothing typed; games, mods, screens and settings found by name;
  the library's games first by mods, then the sites'; what's said of mods; at most 6, 8 and 6; hidden games left out;
  nothing found; the arrow keys and Enter; the highlight kept; a game opening in the Library or Browse, a mod its page,
  a setting its category and a game its settings; searches remembered, newest first, shown before they're saved, and searched again; closing
  forgetting; without the sites' games, keys; a library that can't be read, and a search that can't be saved, logged;
  covers as the Library's previews say; a mod's initial on its game's colour; the keys shown.
- The Library, Browse and Settings opened as quick search opens them; the settings' parts listed by category and
  heading; the category already shown, opened, ending a search too; the main window going to a screen.
- `UiState` and `ui-state.json` keep the searches, one alone too.
- 62 deliberate bugs, all caught. Four got through at first, and tests now catch them: a mod or a game opened without
  going to the Library, as the tests started there; the category already shown keeping the search; two lists of
  searches the same length taken as equal; a single search not saved.
- Looked at here, on Linux, without an account: the box over Browse; `s` finding games by key, mods, Settings and its
  parts; ↓↓; Enter on Stardew Valley opening the Library there; a mod's page; the recent searches; the field collapsed.
- By hand: `docs/manual-checklist-desktop.md` 9.2d.
