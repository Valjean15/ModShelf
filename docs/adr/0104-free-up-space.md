# 104. Free up space, keeping what may be the only copy

## Context

The library only grows. Archived old versions pile up in `_archive` folders, a file can end up in two places (a file
its author uploaded again under a new ID, or the same file in two mods), mods stop being watched with their files left
behind, and files placed by hand in the mods' folders match nothing. Until 1.0.9, the only way to delete a file was
one at a time, from its mod's page.

The human approved, for 1.0.9 (feature 7 of the plan), a **Free up space…** view: for every game or the one shown, the
files that could go, by group, each with its size and a total; the files that may be the only copy left kept, saying
why; deleting what the user chooses after asking. The desktop app and the command line delete, after asking; the MCP
server only lists, so an agent can't delete files.

## Decision

### What could go, by group

`ILibraryCleanup.PlanAsync`, in Application's new `Cleanup` feature, lists only files on disk, at the size they have
there. A file is in the first group that fits:

1. **Duplicates:** library files with the same SHA-256 and the same size on disk. Of each set, one copy is kept: one
   outside an `_archive` folder before one inside, then a watched mod's, then the one downloaded last. The rest are
   duplicates, each naming the copy kept (`CleanupCopy`). They're found across the whole library, so for one game the
   copy kept may be another game's. Empty files and files without a SHA-256 are never duplicates; a rescan reads the
   hashes the library lacks.
2. **Archived old versions:** library files in an `_archive` folder.
3. **Files of mods not watched.**
4. **Files that match nothing:** files in the mods' folders (as a rescan looks in them, with each `_archive`) that the
   library doesn't have and that match none of the mod's files on its site, by name and size, as a rescan matches them.
   A folder several mods share is matched against each of them. They're never the library's, so they're in no other
   group; they're never hashed, so never duplicates.

### Kept, saying why

An archived or unwatched file is kept when it may be the only copy left (`KeptBecause`):

- **ModGone:** its mod is gone from its site (`LibraryMod.IsGone`). Such a mod's site isn't asked.
- **NotOffered:** the site doesn't list the file, or lists it as archived, which it no longer offers for download, as
  the mod page's "only copy" mark has it.
- **NotChecked:** the site couldn't be asked; the reason is kept.

A file that matches nothing is kept when it's one of the mod's files on the site after all (**MatchesSiteFile**: a
rescan adds it), when a mod whose folder holds it is gone, or when a site couldn't be asked. Duplicates need no site:
the copy kept stays.

Each mod's site is asked at most once, `IModCatalog.GetFilesAsync`, and only about a mod with archived, unwatched or
unknown files. A refused account, a request to slow down or a site that can't be reached stops asking that site, as a
rescan does; the files of the mods not asked are kept. A plan asks no quota question first: it can't know how many mods
need asking until it has looked through their folders, and a site that asks it to slow down only keeps more files.

### Deleting

`ILibraryCleanup.DeleteAsync` takes entries a plan listed as able to go, refusing any kept one, and holds the library
(`ILibraryHold`) while it works, so no download is recorded meanwhile. One file at a time:

- **Checked first:** a file is left, as a failure the user reads, when it changed since it was listed: its game's folder
  moved, it's gone or of another size, it left the library or moved within it, it became the library's, its mod is
  watched again, or the copy kept of a duplicate changed or went.
- **A library file** goes through `IModLibrary.DeleteFileAsync`, as deleting it from its mod's page does: from disk,
  then the library, recorded as `Deleted` in the history, its emptied folders removed. A mod left neither watched nor
  with files leaves the library.
- **A file that matches nothing** goes from disk, with the folders it empties, and is recorded as `Deleted` under the
  mod whose folder held it, the first by name when several share it, without a file ID. Recent activity says it matched
  none of the mod's files.
- A file another program has open, or that this user may not delete, is left, saying why.

### Where it's offered

- **Desktop:** **Free up space…** on the Library, for every game or the game open. The dialog lists as it opens, then
  shows each group's heading, with a box that ticks all its files that can go, then its files, each with its own box;
  a kept file's box can't be ticked, and it says why. Only the rows in view are made. Archived old versions and
  duplicates start ticked; files of mods not watched and files that match nothing don't, as the user may want those.
  **Delete selected (size)…** asks first, then deletes and says what it did, and what it couldn't. Stop ends listing or
  deleting.
- **Command line:** `library cleanup [--game <site/game>]` lists the files as a table and each group's total;
  `--delete <archived|duplicates|unwatched|unmatched>` says what deleting that group would do, and with `--yes` deletes
  its files that can go.
- **MCP:** `library_cleanup` lists, a page at a time, with each group's total. It's read-only; the server's instructions
  say deleting files is for the user.

## Consequences

- Space is freed without losing what can't be had again: a file goes only when its site still offers it, a copy of it
  stays, or it matches nothing at all, which the user chooses to delete.
- Listing costs one request a mod with files to judge, as a rescan does.
- New public types, approved: `ILibraryCleanup` and its records and enums in Application. `FoldersOf`, which rescanning
  used, became the shared `ModFolders`. No schema change, no new package.
- `LibraryViewModel` takes `ILibraryCleanup`; `IDialogService` gains `FreeUpSpaceAsync`. The MCP server has 30 tools.
- Log events 2700–2705 are new, in Application's new block for freeing up space; 987 and 988 in Presentation's.

## Testing

- Application: archived files offered or not; a duplicate archived, and which copy is kept; sizes, hashes and empty
  files that aren't duplicates; files of mods not watched; a gone mod not asked; a refusal stopping the site, another
  problem only its mod; unmatched files, one matching a site file, a second copy of a library file, and a folder mods
  share; one game, with the copy kept in another and a file missing from disk; progress; deleting while holding,
  recording each and removing emptied folders; every way a file changed since it was listed; a file that can't be
  deleted; a kept file refused.
- Command line: the table and totals, deleting nothing; what deleting a group would do, then deleting it; nothing to
  free up; refusals. MCP: the listing, its groups and pages, and that it deletes nothing.
- Presentation: the game open or every game; the groups, rows and what's ticked; ticking a group or a file, and a kept
  file refused; asking first, deleting and what it did; Stop; nothing to free up and failures. Recent activity's wording
  for a file that matched nothing.
- By hand: the command line's section 23, the desktop app's section 25 and the MCP checklist's section 12.
