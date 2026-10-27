# 20. Moving the library, and old versions

- Status: Accepted
- Date: 2026-10-27

## Context

Step 3 of Phase 4 (ADR 0018) finishes the plan's storage rules:

- "Move library" relocates files and updates records in one transaction, rolling back on failure.
- Old versions: keep all, keep the last N, or move them to an `_archive` folder.

Until now, the library's root could change only while the library had no files (ADR 0019).

The human chose:

- **What an old version is:** a file the site says a newer one replaced. That adds `ReplacedBy` to `ModFile`, which step 4's update checker reuses.
- **The default rule:** keep the last 2 versions, deleting older ones.
- **What a move takes:** the library's files only. It refuses while library downloads are unfinished, rolls back on an error, and removes the folders it empties.
- **An interrupted move:** running it again finishes it.
- **The public changes:** as proposed before building, and recorded below.

## Decision

### Which file replaced which

- **`ModFile.ReplacedBy`** is the newer file of the same mod that the site says replaced this one. Following it from file to file leads to the newest version. A file can only be replaced by another file of its mod.
- **Nexus Mods** fills it from the `file_updates` list in `files.json`: `{old_file_id, new_file_id}` pairs. In the recorded SMAPI files, 294 files have a newer version, and the chains of 82 lead to 4.5.2. Links with a file ID that isn't positive, or from a file to itself, are skipped.
- **A site without such links** leaves it null, and its files have no old versions.

### The rule for old versions

**`OldVersionRule`** (Domain) is one of three:

- `KeepAll`;
- `Archive`;
- `KeepLatest(n)`, where n is from 1 to 99. `Default` is `KeepLatest(2)`: the newest version and the one before it.

`Parse` reads `all`, `archive` or a count, as `ToString` writes them.

**The setting** is `library.oldVersions` in `settings.json`: a count as a number, the others as text. `AppSettings.OldVersions` holds it, and `ISettingsService.SetOldVersionsAsync` changes it. A changed rule applies to downloads from then on; files already in the library stay as they are.

### Applying the rule

**When a download is recorded**, its old versions are the mod's library files that it replaced, directly or through versions between them, the nearest first. They're found by following `ReplacedBy` in the site's list of the mod's files.

- **Only a file new to the library has old versions**, since that's when the list is read.
- **Downloading an older version leaves the newer ones.**
- **A file the site no longer lists isn't an old version**, and a chain that goes round ends.

**`KeepLatest(n)`** deletes all but the newest n versions. The new file counts as one, and so do archived ones.

- A file is deleted from the disk first, then from the library.
- **A file that can't be deleted**, such as one another program has open, stays in the library, so the next newer version tries again.
- **A file already gone from disk** leaves the library.

**`Archive`** moves old versions into an `_archive` folder in their own folder.

- They stay in the library, with their new paths. Files there already aren't archived again.
- **A name another library file or a file on disk has there** gets the file's ID; when that's taken too, the file stays where it is.
- **A file gone from disk, or one that can't be moved**, stays as it is.
- Files are moved before the library changes. If the library refuses the change, they're moved back.

**Every move and delete is logged** (events 604–609). Failures are warnings and don't fail the recording.

**`ILibraryDownloads.RecordAsync` now returns a `LibraryRecord`**: the file, plus the old versions archived and deleted.

- Recording the same job again returns the same record while the app runs, matched by the file's path.
- So the CLI can report what the recorder did, whichever of them recorded the job first.
- The last 64 records are kept.

**A mod's next file goes into the folder of its latest file.** When that file is archived, the next file goes into the folder holding `_archive`, not into `_archive` itself.

### Moving the library

**`ILibraryMover.MoveAsync(root)`** moves every file the library has to the same place under the new root, then makes it the root.

- **Library paths are relative to the root**, so the library itself doesn't change.
- **The root changes in `settings.json`**, in one step (a temporary file, then a replace), once every file is there. That's the "one transaction".

**Before anything moves, it refuses:**

- a root inside the library, or one that holds it;
- a move while downloads into the library are unfinished;
- a move where a file is already in the place of one of the library's. That includes a file of a different size where one of the library's would be, such as a partial copy from a move that was cut off.

**While it moves:**

- **When a file can't be moved**, the ones moved go back, newest first, and the root stays. The message says whether every file went back. If any couldn't, moving there again finishes the move.
- **A move that stopped part-way**, because it was cancelled or the app or the computer stopped, finishes when it's asked for again. A file under the new root at its size, and gone from the old one, counts as moved. Until then, the root is the old one.
- **Files the library has that aren't on disk** stay in it, to be downloaded again.

**Afterwards:**

- Other files under the old root stay.
- The folders the move emptied there are removed, the old root too.

`MoveFile` never overwrites. A move to another disk copies the file, then deletes the original, as .NET's `File.Move` does.

**`LibraryMove(From, To, Moved, AlreadyThere, Missing)`** says what it did. When the library is already in the new root, nothing changes, and `From` is `To`.

**Setting the root** (`ISettingsService.SetLibraryRootAsync`) still works only for a library without files. It now also refuses while downloads into the library are unfinished, so none finishes outside it, which ADR 0019 left open.

### Storage

`ILibraryStorage` gains:

- `FileSize`;
- `MoveFile`, which makes the folders it needs and never overwrites;
- `DeleteFile`, which does nothing when the file is gone;
- `RemoveFolderIfEmpty`.

`FileSystemLibraryStorage` does them on the file system.

### The CLI

- **`library move <folder>`** says how many files moved, how many were there already, how many weren't on disk, and where the library is now.
  - Ctrl+C stops it with exit code 130, and running the same command again finishes it.
  - Refusals and failures exit with 1.
- **`settings set library.oldVersions all|archive|<n>`** and `settings reset library.oldVersions` say what the rule does. `settings show` lists it, with `2 (default)` when it isn't set.
- **`settings set library.root`** on a library with files ends with `To move it there: library move "<folder>"`.
- **`download`** adds a line for each old version: "Moved the old version … to the _archive folder in its folder." or "Deleted the old version …".

### Logging

The library downloads' block (600–699) also takes:

| ID | Event |
|---|---|
| 604 | An old version was archived |
| 605 | An old version couldn't be archived |
| 606 | An old version wasn't archived, because both names were taken |
| 607 | An old version was deleted |
| 608 | An old version couldn't be deleted |
| 609 | An archived file couldn't go back after the library refused a change |
| 610 | The library was moved |
| 611 | A move failed, and the files moved went back |
| 612 | A file couldn't go back while undoing a move |

## Consequences

- **Deleting is the default.** A download of a newer version deletes all but the two newest versions of that file, for good: there's no Recycle Bin from .NET on every system. `settings set library.oldVersions all` turns it off.
- **Step 4's update checker** can follow `ReplacedBy` from a library file to the newest version on the site. That's what "a new file ID in the same file group" means in the plan.
- **A second process starting a download into the library during a move** isn't stopped; only downloads unfinished when the move starts are. The CLI and a single desktop window never do both at once.
- **A move to a disk without room** fails at the file that doesn't fit, and the files moved go back. There's no check of the room before it starts.
- **`library tidy`, applying the rule to files already in the library,** is left out. The rule applies as newer versions arrive.
