# 97. Browse starts empty, actions on the selected mods, and game lists for hundreds of games

## Context

The human asked (2026-11-17, for 1.0.7) for six changes to the desktop app:

- Browse's sidebar icon was the magnifying glass that quick search's **Search** also shows, so the two were easy to
  confuse.
- Browse chose the game the library has the most mods of as soon as the sites' games were read, and listed its rows: up
  to three requests to a site each time the app started, for a game the user may not have wanted.
- The game chip's × only let the user choose another game: closing the list without choosing put the chip back.
- The recent searches, Browse's and quick search's, could only be pushed out by newer ones.
- The Library's selection could only download updates (**Update selected**); watching, unwatching and removing went mod
  by mod, from each mod's **…** menu.
- Settings › Games started with **Games on this PC**, a row of two lines and a full button per installed game, and both
  its lists grew with the page: a user with a hundred games or more got a page thousands of pixels long, every row built
  and every game's cover read when the category showed.

## Decision

### Browse's icon

- `BrowseIcon` is a globe (its rim, a meridian and the equator), drawn like ModShelf's other icons on the 24 by 24 grid.
  The magnifying glass becomes `SearchIcon`, which the sidebar's **Search** and quick search's field use. Quick search
  lists the Browse screen with the globe, from `ScreenIconConverter`, as the sidebar does.

### Browse starts empty and remembers the game chosen

- **No game is chosen for the user.** Browse lists nothing, and shows a globe with "Choose a game to browse its mods",
  until the user picks one (`BrowseViewModel.HasNoGame`). Choosing a site in the site box still picks that site's game
  the library has the most mods of, as before: the user asked for that site.
- **The game chosen is remembered**, as state rather than a setting (ADR 0066): `UiState.BrowseGame`, a `GameRef`, kept
  in `ui-state.json` as `"browseGame": "nexus/stardewvalley"`. A public addition to Application, which the human
  approved. Every change of game remembers it, quick search opening one included; a value that isn't a site's game is
  left out when read.
- **The next run opens on it** as soon as its site lists it, rather than once every site has answered: a slow site, such
  as DEG Mods' relays, keeps it waiting no longer. A game no site lists now, or one the settings hide, is passed over;
  one of a site that finds its games by name is asked for by name, as quick search's are.
- **The chip's × takes the game away** (`GamePickerViewModel`'s new `cleared` action): the list stays closed, what was
  listing stops, the mod open closes, **Tracked mods** goes, and the game is forgotten. The picker in Settings' dialogs
  has no `cleared`, so its × still lets the user choose another. Choosing a site the chosen game isn't of still opens
  the list with the chip put back on closing (`ChooseAnother`). The picker's field opens its list on a press anywhere in
  it but the ×, which would otherwise open the list before the × could act.

### Clearing recent searches

- **Clear** beside "Recent searches", in Browse's game picker and in quick search, forgets them, there and in
  `ui-state.json` (`BrowseSearches` and `QuickSearches` set empty). The button can't take the keyboard, so typing goes
  on in the field. One that can't be saved is logged (events 977 and 978), and the list is still cleared for this run.

### Actions on the selected mods

- Beside **Update selected**: **Watch (n)**, **Unwatch (n)** and **Remove (n)**, each counting the selected mods it
  would change, greyed out at none (`LibraryViewModel.Batch.cs`). They do what each mod's menu does, one mod after the
  other, through `IModLibrary`, never asking the sites: `SetWatchedAsync` and `ForgetAsync`. No confirmation: as for one
  mod, the undo bar offers to undo unwatching and removing.
- **Undo covers the whole selection**: `UndoBarViewModel.Offer(said, changes)` takes the changes in the order they were
  made, and undoing restores them the last first; one that can't be restored keeps none of the others from it, and the
  first such says why. One mod says its name ("SkyUI removed from the library; …"), several their count ("3 mods removed
  from the library; their files stay in their folders.").
- What couldn't be done, such as a mod whose download isn't finished, is said once, mod by mod, in one message; those
  stay selected, the others are unselected. A mod that left the library meanwhile counts as done. Anything unexpected is
  logged (event 979).

### Settings › Games for hundreds of games

- **Games on this PC goes last**, under the games' own settings, which are what most visits are for.
- **Compact rows**: an installed game is one line, its name, a pill per site with its game, and a quiet **Choose…**
  (`InstalledPill`). A pill names the site alone when the game has the installed game's name, letters and digits
  compared, case aside ("Borderlands® 4" and "Borderlands 4" are the same); otherwise "Site: game". A choice of a game
  is the accent's soft colour, a choice of none ("Site: none") faint, and several games of the name ("Site: 2 games")
  warm. The sentence the row said before stays as each pill's tooltip, and for screen readers.
- **Finding and narrowing**: a box finds an installed game by its name or its games' names on the sites, and **Without a
  site's game · n** shows only those no site has a game for (it turns itself off once there are none). The games' own
  settings get a box that finds a game by its name, with how many are listed ("3 of 124 games").
- **Lists that scroll on their own**: inside a page that scrolls, a list grows to fit every row, so nothing can be
  virtualized. Each list now has a height of its own (440 and 520 pixels) and scrolls inside it, so its
  `VirtualizingStackPanel` builds only the rows shown. A game's cover in the games' own settings is read once its row is
  prepared (`GameRowViewModel.ShowCover`, from the list's `ContainerPrepared`), rather than every cover when the
  category shows.

## Consequences

- Starting the app asks nothing of the sites for Browse until a game is chosen, or the one remembered is listed.
- A user who never picks a game sees an empty Browse; a user who does finds it again, until they take it away.
- The selection can now change many mods at once; undoing them is one step, in the order it takes to put each back.
- Settings › Games stays short and quick with hundreds of games; the installed games' list is the last thing on it.
- One public addition to Application (`UiState.BrowseGame`), approved; no package, no database change. `ui-state.json`
  gains `browseGame`, which older versions keep without reading.
- Log events 976–979 are new, in Presentation's block.

## Testing

- Application: `UiState.BrowseGame` in equality, and none by default. Infrastructure: `browseGame` saved and read back
  as documented, left out when it isn't a site's game (a number, a key without its site, a site without a key, a list),
  and removed when forgotten.
- Presentation, Browse: no game with a library, the game chosen last chosen again and its rows listed, a game no site
  lists or the settings hide passed over, one found by name, one remembered as soon as its site lists it while another
  site is still slow, choosing remembers it, one that can't be saved still chosen, the × taking it away (nothing listed,
  nothing following, forgotten), the site box moving to the remembered game's site, quick search's game remembered; the
  tests that expected the library's game to be chosen at start now choose it, or remember it.
- Presentation, recent searches: Browse's Clear forgets them here and in the state, and clears them even when they can't
  be saved; quick search's Clear only on the recent searches' heading, forgetting them and showing the screens.
- Presentation, Library: what the three buttons say and allow, unwatching and removing several with one undo putting
  them back the last first, watching only those unwatched without undo, one mod named, and refusals said once with the
  mods kept selected.
- Presentation, Settings: the games' filter and count, one game counted as one; covers read only once rows show; the
  installed games' pills (by name, chosen, none), the filter by an installed game's name or its games' names, and the
  toggle, which turns itself off.
- By hand, on Linux under Xvfb with a test library: 130 installed Steam games and 131 games with settings of their own;
  Browse empty, remembered after a restart, its × and Clear; quick search's Clear; the Library's Remove (3) and Undo;
  Settings › Games' lists, filters and pills.
