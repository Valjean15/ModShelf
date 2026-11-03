# 56. The library's history

- Status: Accepted
- Date: 2026-11-03

## Context

The library knew which files it has, not what happened to them: `library_changes` gave a new file with its mod's other
files and left the agent to guess which version it replaced, which `library.oldVersions` may have deleted already
(ADR 0040). The plan's step 13 keeps a history, which says what each download replaced and what became of it, and which
the desktop app shows as the library's recent activity.

The human chose (2026-11-03):

- the history first (13a), then pins, skipped updates and notes (13b), each with its own migration;
- every file event, kept a year: downloads, with the version each replaced, old versions archived or deleted, files
  deleted by the user, files found by rescans, and mods removed from the library;
- the recent activity as a page of the Library screen, from a button beside **Rescan**.

## Decision

- **Application:**
  - `LibraryEvent`: when, its kind (`LibraryEventKind`: `Downloaded`, `Archived`, `Deleted`, `Found`, `Removed`), the
    mod and its name then, and for a file its reference, name on its site, version and path in the library; for a
    download, the version it replaced (`Replaces`, `ReplacesVersion`); for a deletion, whether the rule for old versions
    did it (`ByRule`).
  - `ILibraryHistory.ListAsync(since, limit)`: what happened since a time, or ever, the newest first, what happened
    together in the order it was kept; 1 to 1,000. `ILibraryHistory.Kept`: a year.
  - `LibraryHistory` keeps what the library's services tell it, through the port `ILibraryHistoryRepository`, and
    forgets what's older than a year each time. It's told, not asked: when it can't keep something, it logs it (event
    626), and the library is as it was told, only its history lacking it.
  - What tells it: a download recorded into the library, with the nearest older version in its update chain the library
    has, which it replaced, and the old versions the rule archived or deleted; a file the user deleted; a mod removed;
    the files a rescan found, at the time it found them. Each is kept once the library has changed, never when it
    refuses the change; a download recorded again isn't kept again. Watching, unwatching, update checks and files a
    rescan finds missing aren't kept.
- **Infrastructure:** the migration `AddLibraryHistory` adds the table `LibraryEvents`: `Sequence`, `At` (ticks of UTC,
  which SQLite orders and compares), `Kind` by name, the mod's source, game, ID and name, the file's ID, name, version
  and path, `ReplacesFileId`, `ReplacesVersion` and `ByRule`, with an index on `At`. `LibraryHistoryRepository` adds
  and prunes in one context, and lists the newest first.
- **CLI and MCP:** each file of `library_changes` gains `replaces`: the version it replaced (`fileId`, `version`,
  `name`) and what became of it, `archived` or `kept` as the library has it now, `deleted` when the history says so,
  and `gone` when it left the library otherwise; null when it replaced none, or the history doesn't say. The file's
  download is the history's one at the time the library says it was downloaded, among the newest 1,000 events since the
  time asked. The server's instructions say so.
- **Presentation:** `LibraryViewModel.ShowActivityCommand` shows `Activity` (`LibraryActivityViewModel`) in place of the
  games, read again each time: `Days` (`ActivityDayViewModel`: "Today", "Yesterday" or the long date, in the user's
  time zone) of `ActivityEntryViewModel`s, each with its time, a tag, the mod's name and its game's, what happened
  ("Downloaded SkyUI 5.3 (5.3), replacing 5.2.") and `OpenCommand`, which opens the mod's page there; `Kinds` and
  `SelectedKind` narrow it; `EmptyText`, `Problem` (logged, event 949) and `CappedText` past 1,000. A mod opened from
  the activity goes back to it: the way back, `BackText` (which was `OpenGameName`), says "Recent activity".
  **Library** goes back to the games.
- **Desktop:** **Recent activity** beside **Rescan** on the games; the page with its crumb, heading, **Show** list
  (below the heading in a narrow window), and a list of rows a day.

## Consequences

- A library from before 0.9.3 has no history: its files say nothing of what they replaced, and the activity starts
  with the update. Going back to 0.9.2 leaves the table unread.
- The history grows with what's downloaded, a row an event, and loses what's a year old each time something is kept.
- An event kept just after the library changed can be lost if the app stops between the two, or the database refuses
  it; the library stays right, and the log says what was lost.
- Moving the library, a rescan's missing files and a site's removed files aren't kept: the plan names file events,
  and these are the library's or the site's.

## Testing

- Application: listing, its limits, keeping a year and forgetting what's older, nothing to keep, what can't be kept
  logged, cancellation; a download with the version it replaced and the old versions deleted or archived, keeping all,
  a version that can't be deleted, an older version downloaded, a file downloaded again, the same download recorded
  twice, a library that refuses the change, and a history that can't keep it; a file deleted, the last one, one that
  can't be, a history that can't keep it; a mod removed, one not in the library; watching; files found by a rescan, a
  mod brought in by its link, hashes and missing files; the one history the library and its downloads share.
- Infrastructure: an empty history; every value through a restart, the time as the same instant; the kind by name; the
  order, across offsets and within a time; since and the limit; pruning, a minute either side; a 0.9.2 database
  upgraded with its library and an empty history; the registration.
- CLI and MCP: the versions replaced, deleted and kept by default; archived and gone; one deleted as the next came;
  files found by a rescan, and files forgotten and found again.
- Presentation: the days and entries, in the user's time zone, each kind's text, narrowing, an empty history, one that
  can't be read, then or again, more than 1,000, loading, a read stopped as the app closes, reading again, the way back
  to the games, a mod opened from the activity and back, and from its game.
- 63 deliberate bugs, all caught. Six got through at first: tests were added for a deletion at the download's time, a
  file forgotten and found again, days in another time zone, a history that can't be read the second time, a read
  stopped as the app closes, and pruning a minute before the time to keep from. The database's ran only one namespace's
  tests at first, and were run again with the repositories'; one, removing a log line, didn't build, and was rewritten.
- By hand: screenshots of the activity, a mod opened from it, and a narrow window; `docs/manual-checklist-desktop.md`
  4.10; `docs/manual-checklist-mcp.md` 2.4.
