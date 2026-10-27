# 25. The Library and Updates screens

- Status: Accepted
- Date: 2026-10-27

## Context

Step 3 of Phase 5 (ADR 0023) fills in the Library and Updates screens. The plan gives them:

- **Library:** browse the downloaded and watched mods by game, open the folder, and remove files.
- **Updates:** see what changed, read changelogs, and download updates.

ADR 0023 also put clicking an update notice (ADR 0022) in this step.

The human chose, before anything was built:

- **Three additions to `IModLibrary`, as proposed:** watching and unwatching by reference, deleting a file, and a feed of the library's changes.
- **Deleting a file deletes it from disk,** after asking, and the library forgets it.
- **Clicking a notice moves to Phase 6.**
  - On Windows, a click reliably reaches an app that isn't packaged only through the Start-menu shortcut Phase 6's installer adds.
  - macOS needs the signed app bundle.
  - Phase 6 also brings a running ModShelf's window forward.
  - Until then, the Updates badge shows new updates.
- **The screens, as proposed:**
  - the Library by game, with game names from the sites and a filter;
  - Updates with checking now, downloading, and changelogs read on demand.

## Decision

### The library

**`IModLibrary` gains three methods:**

- **`SetWatchedAsync(mod, watched)`** watches a mod in the library, or stops watching it.
  - Its site isn't asked, so it works offline.
  - A mod left unwatched and without files leaves the library, as with `UnwatchAsync`.
  - Asking for what the mod already is changes nothing.
  - A mod that isn't in the library is refused (`KeyNotFoundException`).
- **`DeleteFileAsync(file)`** deletes one of the library's files from disk, for good, and then from the library.
  - **Disk first,** so a file that can't be deleted stays in the library, where the user sees it. The error is `IOException` or `UnauthorizedAccessException`.
  - A file already gone from disk only leaves the library.
  - The folders the file leaves empty under the root are removed. The root itself stays.
  - Each delete is logged (event 613).
- **`ChangesAsync()`** streams every change to the library from the moment it's called.
  - That covers watching and unwatching, recorded downloads and their old versions, update checks, and deletes.
  - A change that leaves a mod as it was isn't listed.

**How changes are published:**

- The library's writers are `ModLibrary`, `LibraryDownloads` and `UpdateChecker`.
- Each publishes what its `UpdateAsync` did to an internal `LibraryChanges`.
- `LibraryChanges` fans each change out to every watcher, as the download queue's watchers do.
- Neither `ILibraryRepository` nor the database changes.
- Changes made by another process, such as the CLI while the desktop app runs, aren't seen until the app starts again.

### The Library screen

**`LibraryViewModel`:**

- **The list:** the mods are grouped by game, with games and mods ordered by name as the user's culture orders them.
  - It's one list of games' headings and mods, so it's virtualized.
  - It follows `ChangesAsync`, so a recorded download, an update check or a delete shows at once.
  - Rows keep their state, such as being expanded, across changes. The rows are updated with as few changes as it takes (`ObservableCollectionExtensions.SyncWith`).
- **Game names:** a game goes by its key, such as `skyrimspecialedition`, until its site names it.
  - Each game's site is asked once while the app runs, through its `IModCatalog`.
  - Without an account, or offline, that's logged without a stack trace (event 904). Anything else is logged with one (909).
- **A box filters the mods by name.** When it hides them all, the screen says so.

**`LibraryModViewModel`** is a mod. It shows:

- its name and author;
- whether it's watched;
- how many files and updates it has.

Its buttons:

- **Open page** opens the mod's page. When no browser opens, a message gives the address.
- **Open folder** opens the folder of the file downloaded last.
- **Watch or Unwatch.**

Expanded, it lists its files.

**`LibraryFileViewModel`** is one file.

- It shows the file's name, its name on the site, its version, its size, and when it was downloaded, in the user's time zone.
- **Delete…** asks first, naming the file and its size. A file that couldn't be deleted gets a message saying why.

**`IFolderOpener`** (Presentation) opens a folder.

- Desktop's `AvaloniaFolderOpener` uses the main window's Avalonia launcher.
- On Linux the launcher starts `xdg-open` and can't tell whether that worked. So a failed open there goes unreported.

### The Updates screen

**`UpdatesViewModel`:**

- **The list:** the updates are ordered by their mod's name, then newest first.
  - It follows `ChangesAsync`, so what a background check finds shows at once. An update leaves once its file is in the library.
  - The badge counts the updates.
- **Status:** it says when every watched mod was checked by, or that some weren't checked yet.
  - With no updates, it says that none were found, or that no mod is watched.
- **Check now** runs `IUpdateChecker.CheckAsync` and waits for a background check that's already running.
  - It says what it did, such as "Checked 12 mods with 3 requests. 1 new update.", and lists each game or mod it couldn't check, with the reason.
  - It can't run without watched mods, or twice at once.
  - A library that can't be read gets a message saying so. Anything else is logged (event 907).

**`UpdateViewModel`** is one update. It shows:

- its mod;
- its name and file;
- its version and upload date;
- the library's file it replaces.

**Download** queues the update into the library through `ILibraryDownloads`, beside the file it replaces.

- It says one of three things: where the file downloads to, that it's queued already, or why the site refused, in the Downloads screen's words.
- Once the file is recorded, the update leaves the list.

**Changes** shows or hides the update version's entry in its mod's changelog.

- The changelog comes from the site's `IModCatalog`, read once per mod (`Changelogs`).
- When the changelog can't be shown, it says why:
  - the site refused the account, or couldn't be reached;
  - the update has no version;
  - the changelog doesn't mention the version;
  - the site gives no changelogs.
- A read that failed is tried again the next time.

### Shared pieces

- **`UserCommands.RunAsync`** runs a command so that nothing it throws reaches the UI. An unexpected failure is logged, and the user is told it didn't work.
- **`Strings.SomethingWentWrong`** is the generic failure message, renamed from the Downloads screen's so that every screen can use it.
- **`Shell.axaml` gains two styles:** `subheading` for a game's heading, and `attention` for the update count.
- **The rows' buttons line up** through shared column widths.

### Logging

Presentation's block grows:

| ID | Event |
|---|---|
| 904 | A game's name couldn't be read (no account, or offline) |
| 905 | An action on a library mod failed |
| 906 | Deleting a library file failed |
| 907 | Checking now failed |
| 908 | An action on an update failed |
| 909 | Reading a game's name failed unexpectedly |

### Testing

**Application tests:**

- `SetWatchedAsync` and `DeleteFileAsync`, including folders and failures;
- that every writer publishes its changes, while a change that changed nothing isn't published;
- that the feed subscribes when called.

**Presentation tests** use an in-memory library whose changes return once every watcher has handled them. They check:

- grouping, ordering, culture and the filter;
- game names, and that each is asked for once;
- following changes, only on the UI thread;
- each row's text and commands;
- deleting, with its question and failures;
- checking now, its summary and failures;
- downloading an update;
- reading changelogs, once per mod, and again after a failure.

**The app was run on a virtual display (Xvfb)** with a library seeded in its database and files on disk. With no account linked, it:

- listed the mods by game;
- deleted a file after asking, live, and logged it;
- listed the updates with the badge;
- said why the site refused a download and a changelog;
- checked now, listing what it couldn't check.

## Consequences

- **Every Phase 4 flow of the CLI's `library` commands now works from the desktop app,** except moving the library, which step 5 adds to Settings.
- **Watching a mod by its link from the app comes with Browse (step 4).** Until then, a mod joins the library when one of its files is downloaded into it.
- **Clicking a notice is now Phase 6's,** with the installer's shortcut, the signed macOS bundle, and bringing the window forward.
- **Game names cost one request per game each time the app starts.** A name cache could save them later if the quota matters.
