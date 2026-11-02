# 50. The Library by game

- Status: Accepted
- Date: 2026-11-02

## Context

The Library screen listed every mod under its game's heading, with a filter by name; a mod's row expanded to list its
files, each with **Delete…**. The 0.9.3 canvas starts the Library with the games instead: a cover for each, how many
mods it has, the updates waiting, the room its files take and when it was last checked. A game opens on a table of its
mods, with toggles that narrow them, orders, and each mod's version, what to know of it, its files, size, and when its
site last updated it; the mod page (ADR 0048) opens from there. The plan's step 9 builds it. The human approved three
choices:

- placeholder covers for now, a game's initial on a colour of its own, with the recording script asking Nexus Mods'
  v2 API about games, so real covers can follow (9b, when `GameInfo` gains its picture's link: a public change);
- the games first, as the canvas has them, rather than the mods under headings with a way to fold them;
- compact rows that open the mod's page, with its files there, rather than rows that expand.

## Decision

- **Presentation**, in `ViewModels/Library/`:
  - `LibraryViewModel` shows `Games` until one opens (`OpenGame`), then `GameMods`; `Mod` opens over them.
    `ShowGamesCommand` goes back to the games, `BackCommand` to the mod's game, the one opened before or the mod's own
    when a requirement opened another game's mod. A game whose last mod leaves goes, and when it's open, the games
    show again.
  - The games: `GamesFilter` by name; `GameOrders`: most mods (the default), name, most room on disk, updates first;
    ties by name, as the user's culture orders it. `SummaryText` counts the games, mods and room on disk, and says when
    they were last checked.
  - `LibraryGameViewModel`: name, `Initial` and `CoverHue` (from the game's key, the same in every run), mods,
    updates ("Up to date"), size with archived versions and how much of it they take, when its mods were last checked
    ("Not yet"), its heading's summary, and `OpenCommand`.
  - A game's mods: `ModsFilter` by name or author; `ModFilters`, toggles for has an update, watched, has archived
    versions and has files missing, each counting the game's mods it says, and showing only the mods every toggle on
    says; `ModOrders`: name (the default), updates first, updated on the site, size, added to the library; then by
    name. `NoModMatches` when none is left.
  - `LibraryModViewModel` loses its files and expansion, and gains `VersionText` (the newest file not archived, a main
    file first), `FilesText` ("2 files, 1 archived"), `SizeText`, `UpdatedText` and `Statuses`: its updates ("Update to
    1.4" when it replaces a file, "New optional file", "Update", or "3 updates"), its files missing from disk ("1 file
    missing", a problem), then "Not watched", or "Watched only" without files. `ModStatus` and `ModStatusKind` say
    them.
  - When a game opens, and when one of its mods changes, its mods' files are checked on disk, once the library's
    folder is known, and their saved details read for when their sites last updated them; a game opened later drops
    the check. Pictures are shown for the open game's mods only, and stay shown once read.
  - `Dates.Ago`: "just now", minutes, hours, "yesterday" and days up to 30, then the day; a moment a minute or more to
    come is its day, and one less than a minute to come, from clocks a little apart, "just now".
  - `LibraryFileViewModel` is gone. A file is deleted from the mod page: `OwnFileViewModel.DeleteCommand` asks first,
    as the Library did, then deletes it from disk and the library.
- **Desktop:** `LibraryView` shows the games as rows (a small cover with **Thumbnails**) or cards with large covers;
  a game's mods as a table, or as cards; a heading with the game's cover and summary; toggles as chips with their
  counts; and the order in a box beside the previews. In a container narrower than 900, the tables put a mod's
  version, files and size under its author and a game's mods and updates under its name, and drop the other columns;
  narrower than 760, the order and previews go below the filter. `CoverBrushConverter` gives a cover's colour.
  The mod page's files each have a **…** menu with **Delete…**.
- **Recording script:** `-Details` also records the v2 `Game`, `GameExpansion` and `GamesFilter` types, and keeps the
  `Query` fields about games, for 9b.

## Consequences

- No interface of `Sources.Abstractions` or `Application` changed; no migration.
- A file is deleted from its mod's page, one step further than before.
- Games go by their keys until their sites name them, as before. Covers are placeholders until 9b; DLC requirements
  still aren't shown (ADR 0049).
- Each game's settings, behind a gear on the canvas, wait for step 12.

## Testing

- Presentation: each game's numbers and summary, its cover's hue (pinned, as it must be the same in every run), a key's
  capital initial, a game with one update, the orders of games, opening a game and going back, a mod opened over its
  game and back, back from another game's mod, a mod opened and games shown, a mod whose game left going back to the
  games, an open game whose last mod leaves; each mod's version, files and size, statuses, when its site updated it,
  files missing not checked without the library's folder, a game opened later dropping the check before, a changed mod
  checked again; toggles and counts (the game's, whatever the toggles show), the filter by name or author, the orders
  of mods, updates first by name; pictures only for the open game's mods, kept when another game opens; `Dates.Ago`;
  deleting a file from the mod page, refused, failing, or gone meanwhile.
- 62 deliberate bugs in the orders, toggles, counts, statuses, checks, pictures, navigation, game numbers, `Dates.Ago`
  and deleting files, each caught by a test. Ten survived at first: eight got tests; a check that an open game has
  mods was redundant, so it went; and a moment a tick to come, which said its day, now says "just now" up to a minute.
  One didn't build at first and was rewritten.
- By hand: screenshots of the games and a game's mods in both themes, with each preview, in narrow windows, and the
  mod page's **Delete…**; `docs/manual-checklist-desktop.md` 4.2, 4.5–4.7, 4.10, 4.11, 9.1, 9.2 and 9.5.
