# 21. The update checker

- Status: Accepted
- Date: 2026-10-27

## Context

Step 4 of Phase 4 (ADR 0018) checks the library for updates. The plan says:

- `UpdateCheckService` runs as a hosted background service on a configurable interval (default 6 hours) and on demand.
- Each source supplies its own strategy through `IUpdateFeed`. Nexus checks updated mods per game, then diffs files only for mods that changed.
- "Updated" means a new file ID in the same file group, or a newer upload time than the local file. Author version strings are shown as labels, never parsed.
- Results feed an Updates list, with changelogs and a one-click "download update".

Step 5 adds the notifications and the 200-mod quota test.

The human chose:

- **An update:** the newest file of the site's update chain (ADR 0020's `ReplacedBy`). For a main file nothing links, also a newer main file the library doesn't have.
- **A watched mod without files:** a new main file since the last check.
- **Storage:** the updates found go in the library database.
- **The shapes:** as proposed before building, and recorded below.

## Decision

### An update

**`LibraryUpdate`** (Domain) is a newer file of a library mod that the library doesn't have. It keeps:

- the file's name, file name, version label, section and upload time;
- the library file it replaces, or none for a mod that's only watched;
- when a check first found it;
- its page, to download it from.

The page goes beyond the proposed shape. It's there so `library updates` can give a link to download each update.

**`LibraryMod.Updates`** holds a mod's updates, and **`WithUpdates`** replaces them with what a check found. An update found again keeps when it was first found.

- **Only a watched mod has updates.** Each one is of the mod, of a file the library doesn't have, and listed once. It replaces a file the library has, if it replaces any.
- **An update goes away:**
  - when its file comes into the library;
  - when the file it replaces leaves;
  - when the mod is unwatched, which takes them all.

**`UpdateFinder`** works out a mod's updates from its files as the site lists them now. Version labels are never compared.

1. **A library file's update is the newest file of its update chain**, when the library doesn't have it. The update replaces the newest library file on the way, so an old file and a newer one the library has make one update, not two.
2. **A main file in no chain**, since authors don't always link updates, has the newest main file in no chain uploaded after it. Files in chains belong to other lines of files. When two main files the library has lead to the same newer file, it replaces the newer of them.
   - The proposal said "a main file nothing links". Leaving chained files out on both sides keeps the end of one chain from being taken for the update of an unrelated main file.
3. **A mod that's only watched**, or that has only archived files, has the main files uploaded since it was last checked, or since it was added. Updates found before stay while the site lists their files.
4. **Archived files** (ADR 0020) are old versions, so they don't ask for updates.
5. **A file the site no longer lists isn't an update**, and a chain that goes round ends.

### Storing updates

- **The `LibraryUpdates` table** keeps them, one row per file per mod, in the check's order, and goes with its mod.
- Rows are matched by file ID, so an update found again keeps its row.
- The `AddUpdates` migration adds the table, with its index columns in a static field for CA1861, as in `AddLibrary`.
- The library's queries load files and updates with split queries.

### Checking

**`IUpdateChecker.CheckAsync`** checks every watched mod, game by game. Each mod's updates are kept in a change of its own, through `ILibraryRepository.UpdateAsync`, so a file downloaded during the check is never taken for an update.

It asks as little as it can:

- **Through the feed.** For a game whose source has an `IUpdateFeed`, one question to the feed covers the mods last checked within its reach. It goes back to the oldest of those checks, less a margin. Only the mods whose files changed since their own last check, less the margin, are then asked about. A feed that doesn't say when a mod's files changed means asking about it.
- **One by one.** A mod never checked, one checked longer ago than the feed reaches, and any mod of a source without a feed, is asked about on its own (`IModCatalog.GetFilesAsync`).
- **The margin is 10 minutes**, for clocks that differ from the site's. A mod is marked as checked at the time the check started.
- For Nexus Mods, a check of mods checked within 28 days costs one `updated.json` per game, plus one `files.json` per mod with changed files.

**When something fails:**

- **A game whose feed fails, or a mod that can't be asked about,** is a failure and stays as it was: not marked checked, its updates as they were.
- **When a site refuses the account (`AuthenticationFailedException`) or asks to slow down (HTTP 429),** none of its other games are asked this time. Each one gets a failure saying so.
- **A site without a catalog** can't be checked.
- **One check runs at a time.** Another waits for it.

**`UpdateCheck(Checked, Requests, Found, Failures)`** says:

- how many mods it checked;
- how many questions it asked;
- which updates no check had found before;
- which games or mods it couldn't check, as `UpdateCheckFailure(Game, Mod?, Reason)`.

`AddUpdateChecks()` registers the checker.

### Checking in the background

**`UpdateCheckService`**, a hosted service, checks every `updates.interval`:

- first when the oldest check is that old, or at once when a watched mod was never checked;
- then that long after each check.

A check that fails is logged, and the next comes an interval later. Settings that can't be read mean the default.

**`AddBackgroundUpdateChecks()`** registers it, for the desktop app in Phase 5. The CLI checks only when asked, so a `download` never starts a check.

**`updates.interval`** is a number of minutes, hours or days, such as `30m`, `6h` or `1d`, from 15 minutes to 7 days.

- The default is 6 hours.
- `UpdateInterval` reads and writes it. It's in Domain, as `OldVersionRule` is, since Infrastructure and the CLI use it too, and Application may make only its contracts public.
- It's kept in `AppSettings.UpdateInterval` and changed through `ISettingsService.SetUpdateIntervalAsync`.
- `settings.json` stores it as `"updates": { "interval": "6h" }`.

### The CLI

- **`library check`** checks now. It says how many mods it checked with how many requests, then lists the new updates: mod, update, version, upload time, the file it replaces, and the link. It says which games or mods it couldn't check, and then exits with 1.
- **`library updates`** lists every update found, with the link to download each.
  - `--changelog` adds each update's changes. They're the changelog entry named exactly as the update's version label; matching names isn't parsing them. Each mod's site is asked once.
- **Downloading an update** with `download "<link>"` records it, applies the old-version rule (ADR 0020), and takes the update off the list.
- **`settings` takes `updates.interval`.**

### Logging

Update checks take the event IDs 700–799:

| ID | Event |
|---|---|
| 700 | A check finished: mods checked, requests, new updates, failures |
| 701 | A game's feed couldn't be asked |
| 702 | A mod couldn't be checked |
| 703 | A background check failed |
| 704 | The interval couldn't be read, so it's the default |

## Consequences

- **Step 5's notifications** can tell the user about `UpdateCheck.Found`: only the updates no check had found before.
- **Step 5's quota test** counts `UpdateCheck.Requests`. With 200 watched mods across 5 games, a first check asks 200 times, and later checks within 28 days ask 5 times plus once per mod with changed files. Nexus Mods allows 2,000 requests an hour.
- **A newer main file that's a different variant**, such as a main file for another edition of the game, is taken for an update of a main file that nothing links. The human chose that over missing updates authors didn't link.
- **`library list` doesn't count updates.** `library updates` lists them.
