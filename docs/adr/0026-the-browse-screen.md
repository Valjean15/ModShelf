# 26. The Browse screen

- Status: Accepted
- Date: 2026-10-28

## Context

Step 4 of Phase 5 (ADR 0023) fills in the Browse screen. The plan gives it: search a source, view a mod's files, and download or watch. Phase 5 is done when every flow of Phases 1–4 works without the CLI. Besides `mod search` and `mod show`, Phase 3 left two CLI flows that no screen has yet:

- Nexus Mods' tracked mods (`mod tracked`, `mod track`, `mod untrack`);
- a game's update feed (`mod updates`).

Until now, the application looked mods up, watched them and tracked them only from links. Browse finds mods by search, so it already knows their references. Going through a link costs a request for the mod's files, since that's how a link to a mod's page is recognized, and fails for a mod without main files (ADRs 0017 and 0018).

A search needs a game, and nothing listed a site's games, so the user would have had to type a game's key, such as `skyrimspecialedition`.

The human chose, before anything was built:

- **The public API additions, as proposed:** a list of each site's games, and looking up, watching and tracking a mod by reference.
- **Tracking:** a Track button on each mod, and a list of the chosen game's tracked mods.
- **The screen, as proposed,** without pictures of the mods for now.

## Decision

### The game list

**`IModCatalog.GetGamesAsync()`** lists every game the site has mods for, each once: a list for the user to pick a game from.

- **The Nexus source** reads `v1/games.json`, one request for them all, approved games only.
  - It keeps the API's order and lists each domain name once.
  - It leaves out a game whose domain name links can't carry, since it couldn't be searched.
  - A game without a name goes by its domain name, and one without a page gets the page its domain name gives, as `GetGameAsync` does.
- **The list is recorded** (2026-10-30): `games.json`, trimmed to the first three games and the recorded one, without their categories. Its entries have the shape of the game answer. The shared contract tests serve it; tests written by hand still serve the recorded game in lists of their own, for duplicates and games without a domain name.
- **The shared contract tests** check that a source lists only its own games, each once, and lists the games of its own links.

### Application

- **`IModLookup.LookUpAsync(ModRef)`** describes a mod as a link lookup does: its game, details, files and changelog, read at once. There's no linked file, so **`ModPage.LinkedFile` is now nullable**.
- **`IModLookup.GetGamesAsync(source)`** passes the question to the source's catalog.
  - It keeps each source's list once read, while the app runs. A failed read is tried again the next time.
  - Two first reads at once both ask the site.
- **`IModLibrary.WatchAsync(ModRef)`** watches a mod as `WatchAsync(link)` does: it adds the mod to the library, or watches it again, with its name, author and page read from its site.
- **`IWatchlists.WatchAsync(ModRef)` and `UnwatchAsync(ModRef)`** track a mod on its site, or stop.
- A mod or source no registered source can serve this way is an `ArgumentException`, as for searches.

### The Browse screen

**Reading the games.** The games are read the first time the screen is shown, so a user who never opens Browse spends no request on them.

- Each site that can search gives its games, ordered by name as the user's culture orders them. With more than one such site, each game names its site.
- The screen starts on the game the library has the most mods of, unless the user chose one, and searches it.
- When a site can't give its games, the screen says why:
  - a site that needs an account offers **Go to Accounts**;
  - **Try again** reads the games again, as showing the screen again does.

**Searching.**

- **The game box** filters the games as the user types. Choosing a game searches it.
- **The search box** takes words. A complete link, such as `https://…` or `nxm://…`, opens its mod instead.
- **The order** is one of nine: best match, and the most or the least endorsed, downloaded, recently updated and newest. Changing it, or **Include adult content**, searches again.
- **An author** narrows the search too.
- Results come 20 to a page, and **Load more** lists the next. A mod listed already isn't listed again, as when the site's results moved between pages.
- A search started while another is under way drops that one.
- **What the site refuses** is said with its name, such as "Nexus Mods couldn't search: …".

**Opening a mod.** Clicking a mod opens it in place of the list, read afresh from its site. **Back to the list** returns to the list as it was. The mod view shows:

- **Its details:** name, author, game, version, last update, downloads and endorsements, its summary, adult content, and whether it's hidden or removed.
- **Its files, by section, newest first.**
  - Main, update, optional and miscellaneous files show at once.
  - Old versions, archived files and files in no section show when asked, or when the link the user gave is to one of them.
  - Each file says whether the library has it, and whether the link is to it.
- **Its changelog,** every version, in an expander.

**Its actions:**

- **Download** puts a file into the library, as on the Updates screen.
- **Save to a folder…** puts it in a folder the user picks, as on the Downloads screen, which now shares that step (`FolderDownloads`).
- Files of a hidden or removed mod, or without a link, can't be downloaded.
- **Watch** adds the mod to the library with its details from its site. **Unwatch** stops watching it.
- **Track on Nexus Mods** tracks it on its site, or **Untrack** stops.
  - Whether a mod is tracked comes from its site's list, read once, the first time a mod of the site is opened, and kept up to date with what the screen tracks.
  - When the list can't be read, that's logged, and tracking is still offered: tracking a mod already tracked changes nothing.
- **Open page** opens its page, or says its address.

**The tracked mods.** **Tracked mods** lists the mods of the chosen game that the account tracks on its site.

- The list is read afresh, highest ID first: on Nexus Mods, the newest mods.
- Each mod's details are read from its site, four at a time, 20 to a page.
- A mod whose details can't be read is listed by its ID, saying why.

**The library.** Each mod, listed or opened, shows whether the library has files of it and whether it's watched, following the library's changes.

**A game's update feed** gets no view of its own: searching with **Recently updated** shows what changed lately. The update checker still uses the feed.

### Logging

Presentation's block grows:

| ID | Event |
|---|---|
| 910 | Reading a site's games failed unexpectedly |
| 911 | Searching, listing or opening mods failed unexpectedly |
| 912 | An action on an opened mod failed |
| 913 | An action on one of its files failed |
| 914 | A site's tracked mods couldn't be read (no account, or offline) |
| 915 | Reading a site's tracked mods failed unexpectedly |

### Testing

- **Nexus source tests:**
  - the game list's names and pages;
  - each game listed once;
  - games without a usable domain name left out;
  - a refused list.
- **Contract tests:** every source with a catalog lists only its own games, and the games of its links.
- **Application tests:**
  - looking up, watching and tracking by reference, and their refusals;
  - the game list kept once read, and read again after a failure.
- **Presentation tests** use scripted sites, an in-memory library and the real navigation, and check:
  - reading the games, when and how often, and its failures;
  - choosing the game to start on;
  - each search and its query;
  - paging, and dropping a search under way;
  - links;
  - opening mods, and their sections;
  - downloading, watching and tracking;
  - the tracked mods;
  - following the library.
- **The app ran on a virtual display (Xvfb)** with no account linked:
  - Browse said Nexus Mods needs an account, with **Go to Accounts** and **Try again**;
  - a pasted link said why it couldn't be opened;
  - **Go to Accounts** went there.
- **A scratch window showed the screen over made-up mods:** the results, a mod with its files, sections and changelog, a download's note, and choosing another game by typing.

## Consequences

- **Every Phase 3 flow of the CLI's `mod` commands now works from the desktop app.** `mod updates` is covered by searching with **Recently updated**.
- **The game list costs one large request per site the first time Browse is shown** each time the app runs. Nexus Mods lists a few thousand games. A cache on disk could save it later.
- **The tracked mods cost a request per mod listed,** 20 to a page. Showing links to them in `mod tracked`, or names, would need the watchlist to return more than references (ADR 0017).
- **No pictures yet.** Showing them needs a picture loader in Desktop through `IHttpClientFactory`, with a cache. They can come with Phase 6's polish.
- **A second source** lists its games through its catalog, and passes the same contract tests.

## Open

