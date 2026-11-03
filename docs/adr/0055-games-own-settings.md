# 55. Each game's own settings

- Status: Accepted
- Date: 2026-11-03

## Context

Every setting applied to every game, except a game's own folder pattern (`library.gameFolders`, ADR 0019). The plan's
step 12 lets a game set its own folder pattern, old versions, whether and how often its mods are checked for updates,
notices, Browse defaults, and whether it's hidden, each showing "Uses default" in the desktop app, with Override; the
0.9.3 canvas draws them in the Settings screen's Games category, which the Library's gear on a game opens.

The human chose (2026-11-03):

- a `games` object in `settings.json`, each game under its key alone, as the site names it in its links
  (`"skyrimspecialedition"`), as `gameFolders` was; a game with the same key on two sites shares its settings;
  `library.gameFolders` is still read, and moves to `games` when the settings are next saved;
- one record, `GameSettings`, with every override, and one setter for any of them, `SetGameAsync`; `AppSettings.Games`
  replaces `GameFolders`; `IUpdateChecker` gains checking only some games;
- a game whose updates aren't checked is skipped by the background checks and by checking every game, but checking the
  game alone still checks it, and the updates found before stay;
- a hidden game is left out of the desktop app's lists, its mods still checked unless its own setting says not;
- a game's own update interval;
- Browse defaults: the order, Updated within and the page size; no category.

## Decision

- **Application:**
  - `GameSettings`, each setting null for every game's: `Folders`, `OldVersions`, `CheckUpdates`, `UpdateInterval`
    (15 minutes to 7 days, as every game's), `Notify`, `Browse` and `Hidden`; `IsNone` when the game has none of its
    own. `BrowseDefaults`: `Sort`, `Direction`, `UpdatedWithin` (one of `Windows`: a day, 7 or 30 days) and `PageSize`
    (one of `PageSizes`: 20, 50 or 100), each refusing what Browse doesn't offer. Both use `init` properties that check
    each value, so `with` checks them too, rather than the positional record proposed.
  - `AppSettings.Games` by game key in any case; `GameFor`, `FoldersFor`, `OldVersionsFor`, `IntervalFor`,
    `NotifiesFor`, `ChecksUpdatesOf` and `IsHidden` give the game's own setting, or every game's.
  - `ISettingsService.SetGameAsync(game, change)`: the change takes the game's settings and gives its new ones; a game
    left with none is forgotten, and Browse defaults all left as Browse's are left out. The game keeps the key it was
    stored under. `SetLibraryFoldersAsync(game, …)` changes the game's folders alone.
  - Downloads apply the game's own rule for old versions.
  - `IUpdateChecker.CheckAsync()` checks the games whose updates are checked; settings that can't be read mean every
    game, which is logged (event 707). `CheckAsync(games)` checks those games, whatever their settings.
  - The background checks: as before while no game has an interval of its own. Otherwise, every game's interval checks
    the games that follow it, from their oldest check, and each game with its own is checked on it, from its own oldest
    check, at once when one of its mods was never checked; a game whose updates aren't checked is in neither. A notice
    lists the new updates of the games that ask for one: a game's `Notify`, or every game's.
  - Diagnostics count the games with other settings of their own besides folders.
- **Infrastructure:** `settings.json` keeps `games.<game>`: `folders`, `oldVersions`, `checkUpdates`, `updateInterval`,
  `notify`, `browse` (`sort` and `direction` by name in lower case, `window` as `1d`, `7d` or `30d`, `pageSize`) and
  `hidden`. A game's own folders in `games` come before its old ones in `library.gameFolders`, which saving removes.
  What a game's settings hold that this version doesn't know stays; a game left with nothing goes. A wrong value says
  which, such as `games.fallout4.browse.window must be 1d, 7d or 30d.`
- **Presentation:**
  - `IHiddenGames` (`HiddenGames`): the hidden games, as a screen last read the settings, raising `Changed` when they
    differ, so every list follows the Settings screen at once. `IGameSettingsOpener` (`GameSettingsOpener`): tells the
    Settings screen which game, then shows it.
  - Settings, **Games**: `Games` (`GameRowViewModel`: the key, a cover's hue and initial, the name its site gives, and
    "Uses the defaults", "2 settings overridden" or "Hidden"), the library's games, those with settings of their own and
    those added, by name; `SelectedGame`, the first at first; `Game` (`GameSettingsViewModel`), the chosen game's page.
    **Add a game…** lists a game by its key, or chooses it when listed, and saves nothing until a setting is overridden;
    it replaces the games' pattern rows, `GameFolders`. The library's games are read when the screen shows (event 948
    when they can't be).
  - A game's page: for the folder pattern, old versions, update checks, notices and Browse, whether the game has its
    own, what every game's is, and its own to change; `Override…Command` gives it one, starting as every game's
    (Browse's own for Browse, "checked, with the other games" for update checks), and `UseDefault…Command`, or the
    pattern's Remove, gives it back. Update checks offer "With the other games (every 6 hours)" and the intervals the
    Updates card offers. Each change is saved as it's made, the pattern when saved; the page then shows what's stored.
    `IsHidden` hides the game.
  - The search finds the Games category by its settings' names too.
  - Library: the hidden games are left out of the games, the summary still counting them, until `ShowHiddenGames`
    ("Show hidden games (2)", shown while a game is hidden); `OpenGameSettingsCommand` opens the open game's settings.
  - Updates: a hidden game's updates are left out, and the badge doesn't count them.
  - Browse: the game box leaves hidden games out, and doesn't choose one for the library's most mods. Choosing a game
    with Browse defaults starts the order, the time and the page size as they say, Browse's own for any left out,
    without searching; a game without leaves the toolbar as it is. `BrowseChoices` holds the toolbar's lists, which the
    game's page offers too.
- **Desktop:** the Games category's list (`ListBox.games`) and the game's page beside it, below it in a narrow card,
  each setting with a "Uses default" or "Overridden" pill (`Border.pill`) and its button; **Show hidden games** among the
  Library's tools; the gear beside an open game's name. Old versions' choices wrap in a narrow window, in the Library
  category too, where the last was cut off.
- **CLI and MCP:** `games.<game>.folders`, `oldVersions`, `checkUpdates`, `updateInterval`, `notify`, `hidden`,
  `browse.sort`, `browse.direction`, `browse.window` and `browse.pageSize`, set and reset as any setting, each saying
  what it does now. `settings show` lists a game's folders by their older name, `library.folders.<game>`, which still
  works, and its other settings when set. `library check` and `library_check` check every game whose updates are
  checked.

## Consequences

- A settings file saved by this version keeps games' folders under `games`, which 0.9.2 doesn't read: going back to
  0.9.2 loses them (it keeps the rest).
- The settings' key names a game's row until its site names it; a game in both keeps the first met.
- The hidden games are read with the settings by each screen that needs them, and the Settings screen tells the others
  when they change; a settings file edited by hand while the app runs is read again when a screen reads its settings.
- With a game on its own interval, the background checks ask the library which games follow every game's before each
  check, and look at the games with their own intervals each time they wake, at most every 15 minutes.
- Checking a game alone, as the desktop app's Library does, never asked `IUpdateChecker`, so it checks the game
  whatever its settings.

## Testing

- Application: a game's own settings over every game's and their defaults, in any case; `IsNone`; the interval's
  limits; Browse's choices; setting a game's settings, keeping its others and its key, forgetting it, Browse's own left
  out, refusals; a game's own rule for old versions; diagnostics; checking every game but one not checked, checking the
  games asked for, settings that can't be read; the background checks of a game on its own interval, one not checked,
  one never checked, notices of the games that ask for one, and of one game when every game's say none. The checker's
  tests gained a settings service, and the fake checkers the new method.
- Infrastructure: every game setting kept and written as documented; `gameFolders` read and moved, a game's new
  folders first, its old ones kept beside new settings; what else a game holds kept, a game with nothing left out;
  settings written by hand in any case; each wrong value. The documented file now has `games` in place of
  `gameFolders`.
- CLI and MCP: setting and resetting each, what each says, the listing; refusals. The list of settings' names, which a
  wrong name prints, gained the new ones.
- Presentation: the hidden games and the opener; the Games category's list, names, notes and choice, a game's page with
  every game's settings, each override, change and use-default, intervals, Browse's choices, hiding, a failed change,
  the gear, a game the library doesn't have yet, a library that can't be read, no game; the Library's hidden games, the
  toggle, a change from the Settings screen, unreadable settings, the gear; the Updates screen's and badge's; Browse's
  game box, its choice for the library's most mods, a game's defaults and what it leaves out, a game without, no search,
  unreadable settings. The tests of single games' pattern rows now test the game's page, and the search's "notice
  updatés" finds Games too.
- 104 deliberate bugs, all caught by a test but one that changes nothing. In the settings, the checker, the background
  checks, the file and the CLI, 57: six got through at first; tests were added for a game not checked beside games with
  intervals of their own, a game with one mod never checked, no check again before it's due, a check due between two
  wakes, Browse left as Browse's own, and the key the file's `games` gives a game, which caught five; the sixth showed
  a lookup in reading the file that changed nothing, which was taken out. Two didn't build at first and were
  rewritten. In the screens, 47: five got through at first; tests were added for choosing the other games' interval,
  notices overridden while every game's are off, a search started before the game's settings were read, and the hidden
  games read again on choosing a game, which caught four; the fifth chose the same interval another way.
- By hand: screenshots of the Games category (a game with settings overridden, every setting overridden, light, 760
  wide), the Library with a game hidden, a game with its gear, and Browse's game box; `docs/manual-checklist.md` 7.5;
  `docs/manual-checklist-desktop.md` 10.2 to 10.2c.
