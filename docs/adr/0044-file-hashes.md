# 44. File hashes

- Status: Accepted
- Date: 2026-11-02

## Context

The human uses an AI agent to copy the library's new files into a mod manager's folders, such as Fluffy Mod Manager's,
and asked for each file's size and checksum, so a copy can be checked without ModShelf reading the file again.

The human approved, for 0.9.2: SHA-256 in the library, with no files written beside the downloads; recorded when a
download is recorded, and by rescanning (step 5); shown in the MCP server, the CLI and the desktop app. The plan had
assumed the transfer's own check could be reused, but the transfer only hashes a file when its site gives a hash to
compare with, in the site's algorithm: MD5 for Nexus Mods. The human chose to read each file once more when it's
recorded over hashing during the transfer, which would have added a column to the download jobs too.

## Decision

- **Domain:** `LibraryFile.Sha256`, 64 lowercase hexadecimal digits, or null when the file hasn't been read, as for one
  recorded before 0.9.2. The constructor takes it last and optionally, in either case, and refuses anything else.
- **Infrastructure:** the migration `AddFileHashes` adds a nullable `Sha256` column to `LibraryFiles`. No existing
  migration changes. `ILibraryStorage.Sha256Async` reads a file once, front to back, sharing it with programs that
  only read it.
- **Recorded:** when a finished download is recorded, before the library changes, so the change waits on the disk, not
  the database. A file that can't be read, such as one another program has locked, goes into the library without a
  hash, and that's logged as a warning: recording never fails for it. A rescan (step 5) reads the files that have none.
- **Kept:** archiving an old version moves the file, which doesn't change its content, so the archived file keeps its
  hash and isn't read again.
- **Shown:**
  - MCP: `sha256` next to `sizeBytes` in every library file: `library_list`, `library_changes`, `mod_show`'s
    `libraryOnly`, and the files' updates' `replaces`; and `librarySha256` beside `libraryPath` for a site file in
    `mod_show`.
  - CLI: `library list --files` lists each file with its version, size, SHA-256 and path; "-" for one without a hash.
  - Desktop: an expanded mod's files on the Library screen show "SHA-256 …", which can be selected and copied.

## Consequences

- Recording a large file takes a read of it: a 2 GB file takes a few seconds on an SSD. Recording holds the library's
  lock meanwhile, so another file finishing then is recorded after it.
- Files recorded before 0.9.2 have no hash until step 5's rescan reads them.
- The hash says what the file was when it was recorded. A file changed on disk afterwards isn't noticed; a rescan of a
  file with a hash doesn't read it again.

## Testing

- Domain: a hash kept in lowercase, none, and hashes of the wrong length or with other characters refused.
- Infrastructure: a file's hash survives a restart, and a file without one stays without; a 0.9.1 database upgraded
  with its files, which have no hash; the storage's SHA-256 of "abc", as FIPS 180-2 gives it, and a missing file.
- Application: a recorded download with its hash, read once; a file the library had, downloaded again, with its new
  hash; one that can't be read recorded without, with the warning; archived old versions keeping theirs, not read
  again.
- CLI: `library list --files` with a real download's hash; files without one; a library without files. The MCP server:
  a real download's hash in `library_changes`, its archived version's, and `mod_show`'s `librarySha256`, each checked
  against the tools' schemas.
- Presentation: the file's hash text, and none.
- By hand: `docs/manual-checklist-mcp.md` 2.6.
