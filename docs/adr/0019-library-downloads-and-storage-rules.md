# 19. Library downloads and storage rules

- Status: Accepted
- Date: 2026-10-27

## Context

Step 2 of Phase 4 (ADR 0018) gives the library a place on disk. The plan asks for:

- a library root, and path templates with tokens, `{source}/{game}/{mod}/{file}` by default, overridable per game;
- file names made safe on every system, including Windows' reserved names and path length limits;
- a free-space check before a download starts.

Downloaded files also have to reach the library, which until now only watched mods.

The human chose:

- library downloads only: `download` without `--to` saves under the library root and records the file, and `--to` stays a one-off download outside the library;
- the settings in a JSON file next to the database;
- a ModShelf folder in the user's Downloads folder as the default root;
- mod folders named after the mod and its ID, which a mod keeps if its site renames it;
- a `settings` command to show and change the settings.

## Decision

### Settings

- **`settings.json` sits next to the library database**, so a database moved for tests takes its settings along. Its shape is `{"library": {"root", "folders", "gameFolders": {game: pattern}}}`.
  - Reading allows comments and trailing commas.
  - A file that can't be read is an `InvalidDataException` naming the file, and saying to fix it or delete it.
  - Saving keeps keys ModShelf doesn't know, drops an empty `library` section, and writes a temporary file that then replaces the old one.
- **`ISettingsService`** applies the defaults and the rules:
  - The root is an absolute path, stored in full without an ending separator.
  - **The root can change only while the library has no files**, until step 3 can move them.
- **The default root is a ModShelf folder in the user's Downloads folder.** Platform's `IUserFolders` finds it:
  - on Windows, the known folder, which the user may have moved;
  - on Linux, `XDG_DOWNLOAD_DIR` from `user-dirs.dirs`;
  - on macOS, and wherever nothing says, `~/Downloads`.

### Folder patterns

- **A pattern is folders separated by `/`, ending in `{file}`.** The folders mix text with `{source}`, `{game}`, `{mod}` and `{modid}`.
  - The default is `{source}/{game}/{mod} ({modid})/{file}`, such as `nexus/stardewvalley/SMAPI (2400)/SMAPI 4.5.2.zip`.
  - A pattern can be set for every game, and for one game, named as its site names it in links.
  - A pattern is refused with the reason when it has an empty or unknown part, a stray brace, or text some system forbids in a name.
- **Every name is made safe on every system** (`LibraryNames`), not only on this one, so a library can move between systems:
  - Characters some system forbids (`<>:"/\|?*` and control characters) become `_`.
  - Leading spaces and trailing dots and spaces are dropped.
  - Windows' reserved names, such as `CON` or `com1.txt`, get a `_` in front.
  - Folders are cut to 100 bytes of UTF-8 and files to 200, keeping a file's extension.

### Placing a file

`ILibraryDownloads.AddAsync(link)` finds the link's file, as `ILinkDownloads` does, and decides where it goes:

1. **The site needs a catalog**, to name the mod and its file. A plain web link is an `UnsupportedLinkException`.
2. **A file the library has, still on disk, isn't downloaded again.** The result says where it is.
3. **A file already queued for the library keeps the place it was queued for**, whatever the settings say now. So a pasted handoff link, or the same command run again, finds the job.
4. **A file downloaded under the root but not recorded yet** is recorded, and isn't downloaded again.
5. **A file the library has, but that's gone from disk,** is downloaded again to the same place.
6. **Otherwise the site describes the file and its mod.**
   - A mod with files in the library keeps its folder, the folder of its latest file, even if the site renamed the mod.
   - A new mod's folder comes from its game's pattern.
   - The file keeps the name its site gives it, made safe.
   - A file the site doesn't list among the mod's is refused.

**A name that's taken gets the file's ID**: `SkyUI_5_2_SE (35407).7z`. A name is taken when another file of the library, another unfinished download, or a file on disk has it, in any case, because case doesn't matter on Windows and macOS. When that name is taken too, it's an `IOException`.

**On Windows, a path longer than 259 characters is refused** with a `PathTooLongException`, counting the `.part` name it has while it downloads. .NET can go further, but Explorer, archivers and many mod managers can't. Other systems take far longer paths (1,024 bytes on macOS, 4,096 on Linux), so nothing is checked there; a path beyond them fails its download as a storage error.

### Recording files

**A download counts as the library's when it's saved under the library root**, however it was added.

- `LibraryPaths.RelativeTo` decides, with the case rules of this system's paths. `LibraryPaths.Resolve` turns a library path back into a full one. `LibraryPaths` is now public, for step 3 too.

**`ILibraryDownloads.RecordAsync(job)` records a finished download:**

- **The file's details come from the site.** The details read to queue a file are kept in memory until its job is recorded or cancelled, so recording needs no more requests. After a restart, they're read again, which needs the site.
- **A new mod is watched**; a mod in the library keeps its choice, and gets the site's latest name, author and page.
- **Recording the same download twice changes nothing.** A download of a file the library has, made later than the library's record, updates its size and time, and needs nothing from the site.
- **Recording takes a lock**, so the CLI and the recorder recording the same job at once ask the site once.

**`LibraryRecorder`, a hosted service, records downloads while downloads run:**

- At start, it records the finished downloads under the root that the library lacks and that are still on disk, such as one that finished just before the app stopped.
- Then it records each download as it finishes.
- Failures are logged, and recording goes on.

### Free space

- **`HttpFileTransfer` checks the room on the disk once it knows the file's size**, before writing any byte.
  - A resumed download needs room for the rest only.
  - A partial file the server's answer replaces counts as room.
  - A file that won't fit is a storage error, with a log entry saying how much it needs and how much is free. Any partial file stays, so the download can go on once there's room.
- **The room is the disk's, from `DriveInfo`:**
  - on Windows, the drive of the path;
  - on Linux and macOS, the mount point with the longest path the folder is under.
- **When the system can't tell, nothing is checked.** That includes network shares on Windows, and a file whose size the server doesn't give. A full disk then still fails the download as before.

### The CLI

- **`download <link>` without `--to` downloads into the library.**
  - It says where the file goes, and "Added to the library." once it's recorded.
  - A file the library has on disk gets "… is in the library already." with exit code 0.
  - A plain web link is refused with a hint to add `--to`.
  - A path too long, or names all taken, fail with exit code 1, pointing to `settings show`.
- **`--name` works with `--to` only**, since library files keep their sites' names.
- **A link pasted for a handoff goes through the library too**, so it finds the job under the name the library gave it.
- **The CLI runs the recorder whenever it runs downloads.** A download saved with `--to` under the library root is recorded too.
- **`settings show`, `settings set` and `settings reset`** read and change `library.root`, `library.folders` and `library.folders.<game>`.

### Logging

Library downloads take the event IDs 600–699:

| ID | Event |
|---|---|
| 600 | A file was added to the library |
| 601 | A missed download couldn't be added |
| 602 | The missed downloads couldn't be looked for |
| 603 | The recorder couldn't add a finished download |

The free-space refusal is the file transfer's event 109.

## Consequences

- **Step 3's move of the library** lifts the rule that the root changes only while the library has no files.
- **A root changed while a library download is still queued** leaves that download going to the old root, where it isn't recorded. Step 3 deals with queued downloads when it moves the library.
- **Two processes placing files at the same moment can pick the same name**, as with `ILinkDownloads`. The CLI and a single desktop window never do.
- **The desktop app registers nothing yet.** Phase 5 adds the library downloads, the storage, the settings and the recorder to its host, as the CLI does.
- **Free space isn't checked on Windows network shares.** A P/Invoke of `GetDiskFreeSpaceEx` in Platform could check them, if that turns out to matter.
