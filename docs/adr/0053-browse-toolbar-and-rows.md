# 53. Browse's toolbar and a game's rows

- Status: Accepted
- Date: 2026-11-02

## Context

Browse searched a game's mods by words and author, in nine orders, with or without adult content, 20 a page, and
choosing a game searched it at once. The 0.9.3 canvas and the plan's step 11 add a toolbar (more orders, a time
window, the game's categories, the page size) and rows on a game's page before a search: Trending, Recently added and
Recently updated. Step 11a taught the recording script to record what these need; the human recorded them
(2026-11-02, Premium, no warnings). What the recordings show:

- the search sorts by `name`, `size` and `uniqueDownloads` as asked; `size` is the mod's main file's, in KiB, which
  each mod's `fileSize` gives; each mod's `createdAt` is when it was published;
- `categoryName` filters by a category's name, as `v1/games/{game}.json` names them;
- a date in a filter must be written in seconds since 1970, as text: ISO 8601 is ignored there;
- `random` takes a `seed`, but two identical requests gave different mods, so its pages couldn't follow on;
- `v1/games/{game}/mods/trending.json` lists the game's trending mods, as v1 describes a mod: no thumbnails, no size.

The human chose (2026-11-02):

- the time window filters by **updated within** (24 hours, a week, a month);
- Recently added and Recently updated come from the search, with thumbnails and the adult setting; only Trending from
  the older API;
- the rows show **before searching**: a search replaces them, and clearing the words brings them back;
- `SearchQuery` takes a **date**, worked out by the screen, so Load more keeps the same cutoff and a source needs no
  clock;
- **Random is left out**;
- `GameInfo` gains its **categories** and a new capability, **`ITrendingMods`**, lists the trending mods;
- `ModInfo` gains its **size** and **published date**.

## Decision

- **Sources.Abstractions:**
  - `SearchSort` gains `Name` (Z to A, ascending A to Z), `Size` and `UniqueDownloads`, numbered after the others.
  - `SearchQuery` gains `Category` (a name, trimmed, blank for none) and `UpdatedSince` (a date, or null), as optional
    last parameters.
  - `ModInfo` gains `Size` (bytes, not negative) and `PublishedAt`, as optional last parameters.
  - `GameInfo` gains `Categories`: names, in the site's order, each once, blank ones left out; its equality compares
    them one by one.
  - `ITrendingMods.GetTrendingAsync(game)`: the trending mods, adult ones included, which the caller leaves out. The
    shared contract tests check it refuses another source's game.
- **Nexus source:** the new orders and filters in `SearchMods`' variables (`categoryName` EQUALS; `updatedAt` GTE in
  seconds); `SearchMods.graphql` asks for `createdAt` and `fileSize` (KiB × 1024); v1 mods' `created_timestamp`; a game's
  categories but the top one named after the game, which the others sit in (`NexusCategory.ParentCategory`); trending
  from `v1/games/{game}/mods/trending.json`, each of the game's mods once, its category named only when the game's
  categories were read already, so the row costs one request.
- **CLI and MCP:** `mod search --sort` and `mod_search`'s `sort` take the three new orders, and say them.
- **Browse screen:**
  - **Toolbar:** five more orders (Most unique downloads, Largest, Smallest, Name A to Z, Name Z to A); `TimeWindows`
    (Updated any time, in the last 24 hours, week, month), the search's `UpdatedSince` being that long before the
    screen's clock when searching, kept by Load more; `Categories` (All categories, then the game's by name, read when a
    game is chosen, which chooses All categories again; one the site can't say leaves All categories); `PageSizes` (20,
    50, 100), the tracked mods' pages too. Changing any of them searches again, or searches in place of the rows.
  - **Rows:** `Rows` (`BrowseRowViewModel`: title, mods, loading, problem, empty, Show all) and `IsShowingRows`.
    Choosing a game shows its rows rather than searching it: Trending (where the site has `ITrendingMods`; hidden
    otherwise), Recently added and Recently updated (searches by newest and last update), 10 mods each, all three at
    once, adult mods only when asked for. A row the site refuses says why and the others show; one that needs an
    account leads to the Accounts screen. Rows listed for the game and adult setting aren't listed again when shown
    again, unless one failed. Searching, the tracked mods or Show all (Recently added and Recently updated) replace
    them; clearing the words brings them back, dropping a search under way. Changing adult content while they show
    lists them again.
  - **What a mod says besides:** by size, its size; by newest and in Recently added, "Added 3 days ago"; by last update
    and in Recently updated, "Updated 2 hours ago" (`FactText`).
  - Events 945 (a row failed) and 946 (categories failed unexpectedly).
- **Desktop:** the three new boxes in the toolbar; the rows as narrower cards side by side, scrolling sideways, with
  their pictures when the previews show pictures (`Button.modCard.rowCard`); the fact on cards and rows.
- **Recording script:** the random order isn't recorded any more; the schema still records `RandomSortValue`.

## Consequences

- Choosing a game spends three requests (trending, newest, recently updated) and one for its categories, rather than
  one search; a search after it spends one more.
- Saved mod details don't keep a size or a published date: the library database has no column for them, and nothing
  shows them there. No migration.
- `IModLookup`, `IModCatalog` and `IModSearch` are unchanged; Browse finds `ITrendingMods` and the game's categories
  through the source registry, as it finds the catalog for tracked mods.
- Browse defaults (an order, a window) per game wait for step 12.

## Testing

- Sources.Abstractions: the new search fields, a game's categories and their equality, a mod's size and date.
- Nexus, against the recordings: the three new orders, the category, the mods updated in the last week (all since the
  date), a mod's size and published date, the game's categories, trending; and by hand: the variables sent, a top
  category kept or left out, trending mods of another game, repeated or without an ID, a refused list, a bad game.
  The re-recorded searches and details changed some expected numbers, updated with them.
- Cli: the new orders and what they say; the list of orders when one is refused.
- Presentation: the toolbar's choices, the categories by name, again for another game, not those of a game no longer
  chosen, unreadable or failing; searches with them, Load more keeping them, before a game nothing, with the tracked
  mods nothing; the tracked mods' page size; each fact. The rows: listed at once, ten at most, empty, adult content,
  no trending, a refused row and trying again, no account, logged, clearing the words, the search under way dropped,
  Show all, another game replacing them, opening a mod and back, the tracked mods, the library's changes. Choosing a
  game no longer searches it: the tests that searched by choosing now search after.
- 51 deliberate bugs in the search fields, the Nexus variables, sizes, dates, categories and trending, the CLI's
  orders, the toolbar, the rows and what a mod says, each caught by a test. Two tests were made stronger first: a top
  category whose parent is null, and the categories of a game no longer chosen. Two bugs didn't build at first and
  were rewritten; four ran no test at first, for a wrong filter, and were run again.
- By hand: screenshots of the rows (cards, thumbnails, off), the category and order boxes, a category's results by
  size as rows and cards, light and dark, 760 wide; `docs/manual-checklist-desktop.md` 3.2a, 3.3a, 3.3b.
