# 45. Rescanning the library

- Status: Accepted
- Date: 2026-11-02

## Context

The library knew only the files ModShelf downloaded. The human reported a file of Female Nude Blend downloaded with the
site's manual download into the mod's folder, which the library never listed; files recorded before 0.9.2 have no
SHA-256 (ADR 0044); and a forgotten mod's files stay on disk, out of the library (ADR 0042).

The human approved, for 0.9.2: rescan by name and size, reading no file to match; `ILibraryRescan.RescanAsync(Uri? link)`
with its result record; a link to a mod not in the library bringing it in, watched, when its files are found; and
`library rescan [link]`, `library_rescan(link?)` and **Rescan** in the desktop app.

## Decision

- **`ILibraryRescan.RescanAsync(link)`** rescans the mod a link is to, or every mod in the library without one. A library
  mod is found by its page there, without asking its site, as unwatching does. It returns a `LibraryRescan`: how many
  mods, the files added, the files hashed, the files missing from disk, the files that match nothing, and the mods
  whose files couldn't be matched (`RescanFailure`), with why. The count of mods is an addition to the approved shape,
  for the summaries.
- **Where it looks:** the mod's folders, where its library files are, and where its game's folder pattern puts its new
  files, each with its `_archive` folder. A new port method, `ILibraryStorage.FilesIn`, lists a folder's files, leaving
  out a download's partial file and what the server said of it.
- **What it matches:** each file there that the library doesn't know, by name and size, against the mod's files on its
  site that the library doesn't have. The name is the site's file name as ModShelf saves it, made safe, or with the
  file's ID added, as a clashing name gets it, in any case. The size is within 1 KB of the site's, since Nexus Mods
  gives some sizes only in kilobytes; a file the site gives no size for matches by name. A file that matches no site
  file, or more than one, is listed as matching nothing, and left alone. No file is read to match.
- **A match** goes into the library with the site's name, version and category, its size on disk, now as when it came
  in, and its SHA-256. A mod that wasn't in the library comes in, watched, as a download brings one in. Each mod
  changes in one update of the library, published to its changes, and only as it was read: a file a download changed
  meanwhile is left to it.
- **Hashes:** the library's files without a SHA-256 that are on disk are read once; one that can't be read is logged
  and left without. A file with a hash isn't read again.
- **The site** is asked only about a mod with files to match: once for its files, and once more for its name, for a
  mod not in the library. A rescan of a library where nothing was placed by hand asks nothing. A refused account, or a
  site asking to slow down, stops asking that site for the rest of the rescan; the mods not asked are failures that say
  so. Hashes and missing files don't need the site, so they're read whatever it says.
- **It never moves, renames or deletes anything.**
- **Shown:**
  - CLI: `library rescan [link]` prints the files added as a table, then the hashes read, and the paths of the files
    missing and matching nothing; failures on standard error, with exit code 1.
  - MCP: `library_rescan(link?)`, not destructive, idempotent, gives the counts in a sentence, and each file with its
    mod. The server has 21 tools.
  - Desktop: **Rescan** at the top of the Library screen, for every mod, and on an expanded mod with a page, for it;
    each says what it found in a message.
- **Shared rules:** where downloads put a mod's files and what they name them move from `LibraryDownloads` to
  `LibraryLocations`, which rescanning looks by.

## Consequences

- A file downloaded outside ModShelf into a mod's folder can come into the library, as can a forgotten mod's files.
- A file placed somewhere else, or renamed, isn't found; one matching two site files is listed, not guessed.
- A folder several mods share, as with a pattern without the mod, is listed for each, but a file in it is listed as
  matching nothing only when none of them matches it.
- The first rescan after 0.9.2 reads every file recorded before it once, which takes a while for a large library.

## Testing

- Application: a file placed by hand added, with its hash, nothing moved or deleted, the change published and logged;
  the hashes the library lacks read and the missing files said, without asking the site; a file with a hash not read
  again, and one that can't be read left without; names as saved, in any case and with the ID, sizes within 1 KB, and
  other names and sizes not matched; a file the library has elsewhere not added again; the `_archive` folder; a mod
  not in the library brought in, watched, by its link, or left out with nothing found; a link rescanning only its mod;
  mods sharing a folder; a site that refuses, stopping its source, and one that fails for a mod only; a folder that
  can't be listed; an empty library. The shared folder and name rules, and the storage's listing without downloads
  under way.
- CLI: a file placed by hand beside a real download, added with its hash; a forgotten mod brought back by its link;
  missing files and files matching nothing, and the same again; the hashes of files recorded without one; bad links.
  The MCP server: a whole rescan and one by link, checked against the tool's schema.
- Presentation: a mod's rescan by its page, one without a page, settings that can't be read; the whole library's
  summary, nothing found, and an unexpected failure.
- By hand: `docs/manual-checklist-desktop.md` 9.6, `docs/manual-checklist-mcp.md` 3.5.
