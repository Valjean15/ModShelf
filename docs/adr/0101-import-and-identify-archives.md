# 101. Import and identify archives, copying only

## Context

People come to ModShelf with gigabytes of archives another mod manager downloaded, such as Mod Organizer 2's or
Vortex's downloads folder. Until 1.0.8, the library could only take files ModShelf downloaded itself, or found by a
rescan in its own folders, named as a download names them. Bringing an existing collection over meant downloading it
again, and without that, its updates couldn't be tracked.

The human approved, for 1.0.8 (feature 4 of the plan):

- A new source capability, `IFileLookup`, which finds a game's file by a hash of its content. Nexus Mods implements it
  first, with the v1 API's `GET v1/games/{game}/mods/md5_search/{md5}.json`, as Vortex does.
- `ILibraryStorage` gaining MD5 beside its SHA-256.
- The new Application use case, `IArchiveImport`.
- **Copy only**: Move isn't offered, so no original is ever deleted.

ModShelf never installs mods (CLAUDE.md): it doesn't extract the archives or read inside them.

## Decision

### Identifying, the cheapest way first

Each archive (`.7z`, `.zip` or `.rar`, in the folder chosen, not its subfolders) is read once, for its SHA-256 and MD5
together. Then:

1. **In the library already:** an archive whose SHA-256 a library file has is left alone, without asking any site. So
   is a second archive with the same content as one before it (a duplicate).
2. **Mod Organizer 2's note:** the `<archive>.meta` file beside it, an INI file written by Qt's QSettings, names the
   mod and the file on Nexus Mods (`modID`, `fileID`, `repository=Nexus`). It's read leniently: UTF-8 or the system's
   code page, with or without a byte order mark, quoted values and `\x` escapes; only the IDs matter. A note of another
   repository, or with no mod, is passed over. The note is confirmed against the mod's files on the game's site: the file
   with its ID, within 1 KB of its size. One request a mod.
3. **The name:** an archive Nexus Mods named, `Name-<mod>-<version>-<uploaded>.7z`, gives the mod's ID; it's confirmed by
   the name and size of one of the mod's files, as `library rescan` matches files. One request a mod, shared with notes.
4. **The hash:** what's left is looked up by the hash the game's site finds files by (`IFileLookup.LookupAlgorithm`),
   one request an archive. Of several files with the same content, the one named as the archive is, or else the one of
   a mod the library has, is it; otherwise the archive stays unknown.

A note or a name that misleads, such as a Nexus-style name for another site's game, or a mod the site doesn't have,
costs its request and falls through to the hash. A mod new to the library is read once more, for its name and author,
unless the lookup described it. A refused account, a request to slow down, or a site that can't be reached stops asking
the site; the archives left say why. Before any of it, the scan says about how many requests identifying takes: one or
two a mod named, one an archive left. The desktop app asks first when that's more than the site's tightest allowance
has left, as Update all does.

### Copying in

- **Where a download of it would go:** the folder pattern's folder for a mod new to the library, or the folder of a mod
  the library has, under the site's name for the file, or with its ID added when that's taken.
- **Through a partial file:** `ILibraryStorage.CopyFileAsync` writes beside the destination under a name a rescan
  doesn't list, checks the copy's SHA-256 against the one read when identifying, and renames it into place. A copy that
  fails, is cancelled, or whose archive changed since, leaves nothing; the original is only ever read.
- **Recorded as found:** the file goes into the library with its SHA-256 and the site's details (version, section,
  upload time), and the history records it as `Found`, as a rescan's. A mod new to the library is watched; a mod the
  library has keeps its own watching, as list import does.
- **One archive at a time**, each holding the library (`ILibraryHold`), so it never races a download being recorded or
  the library moving.
- **Stopped halfway, run again:** what was copied is in the library by its SHA-256, so identifying again finds it there
  and asks nothing about it. An archive the library has by the time it comes up is passed over.
- An archive that can't be copied, such as for want of room on the drive, doesn't stop the others; the result says why.

### Where it's offered

- **Command line:** `library import-archives <folder> --game <site/game> [--yes]` says how many requests identifying
  takes, identifies, and lists each archive and what importing would do; with `--yes`, it copies. Ctrl+C stops it; run
  again, it goes on. A refused folder, one in the library, or a game whose site can't list its mods' files, exits 2.
- **Desktop app:** **Import archives…** on the Library, beside **Import list…**, asks which game (as Settings' Add a
  game… does), and on a game's page takes that game; then the folder. Its dialog says how many archives and requests,
  **Identify**, then what each is and what importing would copy, **Copy into the library**, with **Stop** throughout.

### Ports

- `ILibraryStorage.HashesAsync`: SHA-256 and MD5 in one read (`FileHashes`). MD5 isn't for security: it's how Nexus Mods
  names a file's content.
- `ILibraryStorage.CopyFileAsync`, as above, and `ILibraryStorage.ReadSmallFileAsync`, to read a note whole. Both were
  added with MD5, since copying only needs a copy, and reading a note needs a read; neither deletes anything.
- `IFileLookup.FindByHashAsync(game, hash)`, with `FoundFile(ModInfo, ModFile)`, so a lookup describes the mod and the file
  in one request.

### Not done

- **No Move**, as approved: the originals stay.
- **No subfolders**, and no game detected: the game is chosen. Mod Organizer 2's `gameName` isn't the site's key, and
  Vortex keeps a folder per game already.
- **No MCP tool:** an agent can't import a folder of archives.
- A library file that's missing from disk isn't restored from an identical archive: the archive is in the library
  already as that file, and left alone. Rescan says the file is missing.
- Only Nexus Mods looks files up by hash; on other sites, only notes and names identify, and those are Nexus Mods' own.

## Consequences

- Moving from another mod manager no longer means downloading everything again, and the archives brought over get
  their updates tracked.
- A folder of archives costs one read of each, one request a mod named (two when it's new to the library), and one an
  archive left; each one copied is read once more as it's written.
- New public types, approved: `IFileLookup`, `FoundFile` in Sources.Abstractions; `IArchiveImport` and its records, and
  `FileHashes`, in Application. Three new `ILibraryStorage` members. No schema change, no new package.
- `LibraryViewModel` takes `IArchiveImport`. `AddGameViewModel` takes the words it asks in, for the game the archives
  are of. `IDialogService` gains `ImportArchivesAsync`.
- Log events 2400–2407 are new, in Application's block for archives; 984 and 985 in Presentation's.

## Testing

- Nexus Mods, against hand-written answers: a file found with its mod and MD5, an MD5 it doesn't know, several files,
  incomplete or of another game, and a refused account; the contract test for every source with the capability.
- Infrastructure: both hashes in one read; a copy into folders it makes, the original left; never over a file, nor kept
  when changed, cancelled or missing; a small file read whole, not a larger one.
- Application: notes in odd encodings and repositories; names; the scan asking nothing; identifying by note, name and
  hash, the library's and duplicates without asking; a misleading note or name; several matches; a refused account;
  copying where downloads go, watching new mods only; stopping halfway and going on; an archive that can't be copied;
  500 archives taking the requests the scan said.
- Command line: the preview changing nothing; importing, the folder left as it was, and again, all in the library; a
  folder without archives; refusals.
- Presentation: the game open or one chosen; the folder; why a folder can't be imported from; the dialog's stages, rows
  and facts; asking first when requests look too few; Stop; a site that can't look up.
- By hand: the command line's section 20 and the desktop app's section 22, with Mod Organizer 2's downloads.
