# 106. Show what an archive holds, extracting nothing

## Context

People want to see an archive's files and its readme before deciding what to do with it: which of a mod's files they
need, what a patch changes, what the author says to do. Until 1.1.0, ModShelf could only open the folder an archive is
in, and the user had to open it in another program.

The human approved, for 1.1.0 (feature 9 of the plan), **Show contents** on each library file's **…** menu: a tree of
the archive's folders and files with their sizes, and its readme or another text file shown inline; `library contents
<link>` on the command line; `library_contents` on the MCP server, read only. The approval covers a new Application
port that reads archives, and the NuGet package SharpCompress 0.50.8 (MIT) for .7z and .rar; .zip goes through .NET's
own `System.IO.Compression`.

The plan also asked for a site's own content preview of a file not downloaded yet, where its API offers one, and the
human approved a capability interface for it, Nexus Mods first. Nexus Mods' API gives each file a
`content_preview_link`, but in November 2026 every one tried (SMAPI's 2.5.5, 4.5.1 and 4.5.2) answered 404, its v2
GraphQL `ModFile` has no such field, and the preview's JSON changed format recently. It's left out until a recording
shows the links work: nothing in Sources.Abstractions changed.

## Decision

### Reading, never extracting

ModShelf stays a file manager: an archive is read where it is, and nothing is ever written.

- **The port,** `IArchiveReader` in Application's `Contents/Ports`, lists an archive's entries and reads the first bytes
  of one of its files, up to a limit. Infrastructure's `ArchiveReader` reads .zip with `ZipFile.OpenRead`, and .7z and
  .rar with SharpCompress's `ArchiveFactory`, off the caller's thread, stopping between entries when asked to.
- **An entry's path is only shown.** It's normalized for showing, `\` made `/`, empty and `.` parts dropped, and never
  joined to a path on disk, so an entry such as `../../outside.txt` is a name in the list and nothing more.
- **The kind of archive is its extension:** `.zip`, `.7z` or `.rar`. A file whose bytes aren't what its name says is
  damaged, not tried as another kind.
- **Trouble is said, not retried** (`ArchiveUnreadableException` and its `ArchiveTrouble`): an archive whose list of
  files needs a password, or a file in it that does, is `Encrypted`; one that's damaged, cut short or not what its name
  says is `Broken`; another kind of file is `NotAnArchive`; and a file asked to be read as text that isn't is `NotText`.
  ModShelf never asks for a password. The log says which, once, at Information (event 2801).

### What's listed

`ILibraryContents.ListAsync` takes one of the library's files, checks it's on disk, and lists it as `ArchiveContents`:

- every folder once, those only implied by their files' paths too (a .rar or a .zip may list only files), ordered by
  path, folders first within a folder, in any case first, then exactly;
- each file's size once unpacked, how many files and folders, and their sizes together;
- **its readme:** of the text files whose names say so (`readme`, `read me`, `read_me`, `liesmich`), the one nearest the
  archive's top, one whose name starts with "readme" before others.

Text files are the kinds mods carry as text: `.txt`, `.md`, `.nfo`, `.log`, `.ini`, `.cfg`, `.conf`, `.toml`, `.json`,
`.xml`, `.yaml`, `.csv`, `.lua` and the like (`ArchiveEntry.IsText`).

### Reading a text file

`ILibraryContents.ReadTextAsync` reads at most the first 256 KB of a file (`MaxTextBytes`), and says when that's not all
of it. The text is UTF-8, or UTF-16 when it starts with UTF-16's byte order mark, or else Latin-1, the way Windows
editors save older readmes; a character the 256 KB cut splits is left out rather than shown wrong. A NUL byte in the
first 8,000 bytes means it isn't text.

### Where it shows

- **The desktop app:** **Show contents…** on an archive's **…** menu on its mod's page, for a .zip, .7z or .rar on disk.
  The dialog lists the archive as it opens: the tree on the left, the top folder open when it's the only thing at the
  top, each folder's size its files' together, and the readme chosen and shown on the right; any other text file is
  shown when chosen, the one chosen last winning. Only the rows of open folders are made, and only those in view drawn,
  so an archive of 100,000 files stays quick. **Open or close every folder** does what it says. The window keeps its
  size while the archive is read, so it opens centred, and can be made larger.
- **The command line:** `library contents <link> [--file <name or ID>] [--read <path>]` prints the tree, each file with
  its size, then how to show its readme; `--read` prints a text file instead, saying on standard error when only its
  start is shown. A mod with more than one archive in the library needs `--file`, and lists them.
- **The MCP server:** `library_contents`, read only, gives a page of the entries (500 unless `limit` says otherwise, up
  to 2,000), and the readme's text, or another text file's that `read` names, cut at 32,000 characters for the agent's
  context.

The mod is found by its link without asking its site (`ILibraryContents.FindModAsync`), as the library's other offline
commands find it.

## Consequences

- ModShelf still never extracts or installs anything; the archive on disk isn't changed, not even its times.
- A solid .7z or .rar is decompressed from its start to read a file in it, which takes longer the further in the file
  is; listing is quick either way.
- Multi-volume archives (`.part1.rar`, `.7z.001`) are read as their first volume only, which SharpCompress lists as
  incomplete or damaged.
- SharpCompress is the only new package. It's used in Infrastructure alone, behind the port.

## Testing

- `ArchiveReaderTests` (Infrastructure), over fixtures in `tests/ModShelf.Infrastructure.Tests/Archives/Fixtures`: a
  solid .7z, a RAR 5 archive written by a script from RARLAB's description of the format (WinRAR's `rar` can't run in
  CI), .zip archives the tests write, a Cyrillic file name, a .7z and a .rar whose lists need a password, a .7z and a
  .zip whose files do, archives cut short, files that aren't what their names say, entries with `\`, `.` and `..`
  (nothing is written beside the archive), and a .zip of 100,000 entries listed in under 10 seconds.
- `LibraryContentsTests` (Application): implied folders, the order, counts and sizes, the readme picked, refusals, the
  log, decoding UTF-8, UTF-16, Latin-1 and a cut character, and text that isn't.
- `FileContentsViewModelTests` and `OwnFileViewModelTests` (Presentation): the tree, opening and closing folders, the
  readme read first, the text file chosen last winning, each trouble said, 100,000 files shown as the rows of the open
  folders only, and the menu item only for an archive on disk.
- `LibraryContentsCommandTests` and `McpLibraryContentsTests` (CLI): the tree, reading a file, naming an archive, and
  what's wrong.
