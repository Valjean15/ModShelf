# 84. A big game opens and returns quickly

- Status: Accepted
- Date: 2026-11-09

## Context

The human reported (2026-11-09) that going back to the Library on a game with many mods took a while. With the database
off the UI thread (ADR 0082, 0083), what was left was measured here on a library of 630 mods, 450 of them one game's,
in a Debug build under Xvfb:

| | 1.0.0 | Steps 1–3 | This step |
|---|---|---|---|
| Rows: the game's settings, another process holding the database | no answer for 24 s | 0.7 s | 0.7 s |
| Cards: leaving the game for Updates | over 60 s | over 100 s | about 3–5 s |
| Cards: back to the game | over 60 s | over 300 s | about 2–3 s |

Rows were quick already once the database didn't wait (their list is virtualized); **cards weren't**. A sample of the UI
thread while it was busy: `ContainerQueryActivatorBase.Visual_DetachedFromVisualTree` → `MulticastDelegate.RemoveImpl`.
Avalonia matches a `ContainerQuery` against **every control within the reach of the styles it's in** (decompiled:
`ContainerQuery.TryAttach` calls `Query.Match(target, …, subscribe: true)` for each), whatever its inner selectors say,
and each such control subscribes to the container. The Library's three queries sat in the screen's own styles, so each
of a game's 450 cards' dozens of controls subscribed, and each one leaving removed itself from a list of tens of
thousands: quadratic, minutes.

The rest of a game's showing read more than it needed: Settings and the check of which sites are in use read every mod
with every file (`IModLibrary.ListAsync`) to count games, and opening a game read each mod's saved details one by one.
The human approved (2026-11-09) the two shapes below.

## Decision

### Container queries on what holds their controls

- **Library:** the narrow window's places for the header's buttons, the filter rows' tools and the activity's kind are
  in the `Styles` of the grid that holds each; the narrow columns of the tables are in the `Styles` of each table
  (`Border.list`), whose rows are virtualized. A game's cards are within no query's reach.
- **Updates:** the heading's tools in their grid's styles; an update's **Skip** and **Download** in their own panel's.
- **Browse:** the search row in its grid's styles, a problem's buttons in their panel's: a page of cards is within no
  query's reach. **Downloads:** the link row in its grid's.
- **Settings and a mod's page** keep theirs in the view's styles: a page's few controls, never a list's hundreds.
- **An architecture test** (`ContainerQueryTests`) fails on a container query in a view's own styles, but those two's.

### The library's games in totals

- **`IModLibrary.ListGamesAsync`** and **`ILibraryRepository.ListGamesAsync`** give **`LibraryGameTotals`** (`Game`,
  `Mods`, `ModsWithFiles`, `SizeInBytes`): a row a mod with its files counted and summed by SQLite, then a row a game.
- **Settings' games** and **`SitesInUse`** read them in place of every mod.

### A game's saved details at once

- **`IModDetails.GetSavedAsync(IReadOnlyCollection<ModRef>)`** and **`IModDetailsRepository.FindAsync(mods)`**: a query
  a game, `IN` its mods' IDs. Details that can't be read are left out and logged (event 1563); a database that can't be
  read gives none, logged (1562).
- **The Library's check** of an opened game reads them so: one read for 450 mods, not 450.

### Pictures off the caller's thread

- **`DiskPictureCache`** looks for a saved picture and marks it as just shown, counts the cache's size, and clears it on
  the thread pool: a screen asks for a picture for each mod it shows, from its UI thread.

## Consequences

- A game's cards still take a moment to build, all at once: they're not virtualized, as Avalonia has no virtualizing
  grid; rows are. Virtualizing cards, as rows of cards in a list, is left for later.
- Each card's picture still reads its mod's saved details itself, now on the thread pool.

## Testing

- The architecture test: no container query in the Library's, Updates', Browse's or Downloads' own styles; the views
  allowed them exist.
- The narrow window, looked at here: the Library's games and a game's mods, Updates, Downloads and Browse, each as
  before.
- `ListGamesAsync`: games with mods with and without files, their sizes, archived versions counted; an empty library.
  Settings' games and the sites in use read no mod (`ModsListed`).
- The details of many mods: of two games, unsaved and repeated ones, none asked; details that can't be read left out
  and logged; a database that can't be read. The Library asks once for an opened game's mods.
- Both new repository methods open the database off their caller's thread (`CallersThreadTests`).
- A saved picture is looked at, and the cache counted, off the caller's thread: the clock its marking reads is read on
  another thread.
- 14 deliberate bugs, all caught. One didn't build at first, grouping by a site's key twice, and was written again.
