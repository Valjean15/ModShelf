# 18. The library

- Status: Accepted
- Date: 2026-10-27

## Context

Phase 4 turns downloads into a managed library: every file has a known source, a known location and a known update status. The plan names four entities (`LibraryMod`, `LibraryFile`, `WatchedMod`, `DownloadRecord`), says a mod can be watched without being downloaded, and gives the library a root with folder templates.

Phase 4 has five steps:

1. the library model and watching mods (this ADR);
2. library downloads and storage rules: the root, folder templates, safe names and free space;
3. moving the library, and old versions;
4. the update checker;
5. OS notifications and the 200-mod quota test.

## Decision

### Two entities

**`LibraryMod`** (in `Domain/Library`) is a mod the user watches for updates, has downloaded files of, or both. It keeps:

- the mod's name, author and page, as its site gave them last;
- whether it's watched, which means checked for updates;
- when it was added, and when it was last checked;
- its files.

**`LibraryFile`** keeps:

- the file's name, version label and section;
- when it was uploaded, and when it was downloaded;
- its size;
- its path relative to the library root.

The plan's other two entities are folded in:

- **A watched mod is a `LibraryMod` with `IsWatched` set.** It has no files until something is downloaded.
- **The download history stays in the `DownloadJobs` table.** A separate `DownloadRecord` would repeat it.

The rules:

- **Both are immutable.** Every change returns a new instance, so whoever holds one sees it as it was read.
- **A change that leaves a mod neither watched nor with files returns null**, because nothing keeps it in the library.
- **Library paths are relative, with `/` between folders**, so moving the root, even to another system, moves every file with it.
  - A path is refused if it could leave the library: blank parts, `.`, `..`, a `\`, or a `:`.
  - Two files of a mod can't share a path, whatever its case, since case doesn't matter on Windows and macOS.
- **Files keep the order they were added in.** A file added again replaces the one with the same ID, in place.

### Storing it

- **Two tables:**
  - `LibraryMods`: one row per mod, unique by source, game and mod.
  - `LibraryFiles`: one row per file, unique by mod and file ID. A mod's files are deleted with it.
- **Both tables keep insertion order** with an autoincrement key, as `DownloadJobs` does.
- **`ILibraryRepository.UpdateAsync(mod, change)`** reads a mod, applies a change, and stores the result, all in one transaction. It returns a `LibraryChange` with the mod before and after.
  - SQLite's immediate transaction takes the write lock before reading. So changes made at the same time, even by two processes, run one after the other and are all kept.
  - The change is a plain function: anything it needs from a site is fetched before the transaction starts.
- **Files are matched by ID**, so a file that stays keeps its row.
- **A row no library mod can have stops the load** with an `InvalidDataException` naming the mod, as for jobs (ADR 0005).
- **The `AddLibrary` migration adds the tables.** Its composite index columns are static fields rather than inline arrays, for CA1861. That's the only edit to the generated code, made before the migration was committed.

### Watching

`IModLibrary` lists the library, finds a mod in it, and watches or unwatches the mod a link is to, or the mod of the file a link is to.

- **Watching** finds the link's source, as `IModLookup` does, and reads the mod's name, author and page from its catalog.
  - A new mod is added, watched.
  - A mod already in the library is watched again, with its details refreshed and its files kept.
  - A link no source knows, or whose site has no catalog (such as a plain web link), is an `UnsupportedLinkException`.
- **Unwatching** needs no catalog.
  - A mod without files leaves the library.
  - A mod with files stays, unwatched.
  - A mod that isn't watched, or isn't in the library, is left as it is.

### The CLI

- **`library list`** shows the library by game and then by name. Each row has the mod's ID, name, whether it's watched, how many files it has, and its link.
- **`library watch <link>`** and **`library unwatch <link>`** say what they did, including whether downloaded files keep a mod in the library.

## Consequences

- Nothing adds files to the library yet. Step 2 makes `download` without `--to` save into the library and record the file. Downloaded mods will be watched by default.
- Step 4's update checker reads `IsWatched` and `CheckedAt`, and sets `CheckedAt` through the same transaction.
- **Watching is local.** It's separate from a site's own list, such as Nexus Mods' tracked mods (ADR 0017). Syncing the two is left for later.
- **Watching from a link to a Nexus Mods page** asks for the mod's files, as recognizing such a link always does. So a mod without main files needs a link to one of its files, as for tracking.
