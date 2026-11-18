# 98. Mod lists, and collections read as lists

## Context

The survey of what users need from a mod file manager (2026-11-18) put a portable library first: people want to back
their library up, move it to another computer, share what they use, and start from what someone else put together,
such as a Nexus Mods collection. The human approved, for 1.0.8:

- **Export and import a list** of the library's mods and their files, from the desktop app, the command line and the
  MCP server, showing what importing would do before anything happens.
- **A Nexus Mods collection's files** imported the same way: downloaded into the library, never installed.
- The new public interface in Application (`IModLists`) and the new source capability (`IModCollections`).

ModShelf never installs mods (CLAUDE.md): it doesn't extract, deploy, order plugins or run a collection's install
steps. A collection is a set of files at pinned versions plus instructions for a mod manager; ModShelf takes only the
first part.

## Decision

### The list file: `modshelf-list`, version 1

- **JSON**, written indented, read leniently: trailing commas and comments are allowed, fields a later version adds are
  passed over, and a file of a newer version is refused, saying to update ModShelf.
- **By the sites' own IDs, never by download links**, which expire: a list stays good for years.
- The file:

  ```json
  {
    "format": "modshelf-list",
    "version": 1,
    "title": "Optional: what the list is",
    "createdAt": "2026-11-18T12:00:00.0000000+00:00",
    "mods": [
      {
        "site": "nexus", "game": "skyrimspecialedition", "mod": "12604",
        "name": "SkyUI", "author": "schlangster", "page": "https://www.nexusmods.com/skyrimspecialedition/mods/12604",
        "watched": true, "pinned": true, "note": "Needs SKSE.",
        "files": [{ "file": "35407", "name": "SkyUI 5.2", "version": "5.2", "category": "main", "optional": true }]
      }
    ]
  }
  ```

- `site`, `game` and `mod` are required; everything else is optional. `watched` defaults to true, `pinned` and
  `optional` to false. Numeric IDs are read as text. A page is kept only when it's an `http` or `https` link. A mod
  listed twice is read once, with the files of both. `category` is one of `main`, `update`, `optional`,
  `miscellaneous`, `old-version`, `archived` and `unknown`.
- **Export** writes the library's mods the scope takes (the whole library, a game, or chosen mods), each with its
  files but the old versions archived in `_archive`, by site, game, then name. Nothing is asked of any site.

### Importing a list

- **A plan first, from the library alone** (`IModLists.PlanAsync`): each mod new to the library, in it already, or of a
  site ModShelf can't reach here; each file in the library or not; the optional files counted apart.
- **Then the import** (`ImportAsync`), a mod at a time in the list's order:
  - A new mod listed as watched is watched (`IModLibrary.WatchAsync`), with the list's pin and note. With **Only
    watch**, every new mod is watched. A mod the library has keeps its own watching, pin and note: a list never
    overrides the user's own choices.
  - With downloads, the mod's files are read from its site once, and each listed file the library lacks is queued for
    it, as a link to the file would queue it. A file the site no longer offers is reported, not replaced, unless the
    user asks for the mod's newest main file instead. Optional files are left out unless asked for.
  - What can't be done to one mod doesn't stop the others: the result lists each problem (`noSite`, `siteFailed`,
    `fileGone`, `notDone`), and its mod and file.
  - A host gives the function that queues a link: the command line queues here, with the queue running in the
    command; the MCP server sends each link where `download` sends it, the desktop app when it runs.
- **Before downloading**, the desktop app asks when a site's requests left look too few, as **Update all** does: one
  request to watch each new mod, one to read each mod's files, and two for each file's download.

### Collections

- **`IModCollections`**, a capability in Sources.Abstractions: `GetCollectionAsync(link)` reads the collection a link
  is a page of, at the revision the link names or the latest published, as a `CollectionRevision`: its name, game,
  page, revision, its files (each a `CollectionFile`: the file, its mod's name and author, its name, the version the
  collection pins, its section, and whether the collection marks it optional), what it needs from elsewhere
  (`CollectionResource`: a name, a page if any, optional or not), and how many files it names that the site no longer
  offers. A link that isn't one of the site's collections answers null without a request.
- **Nexus Mods implements it** with one query to the v2 GraphQL API, `Collections/CollectionFiles.graphql`:
  `collectionRevision(slug, domainName, revision, viewAdultContent: true)`, with each file's mod and game. Adult
  collections are read too: the user named this one. It reads `https://www.nexusmods.com/games/{game}/collections/{slug}`
  (and `/revisions/{n}`, or a tab, after it), the older `https://next.nexusmods.com/{game}/collections/{slug}`, and
  the `nxm://{game}/collections/{slug}/revisions/{n}` links the website's button hands over. A file listed twice is
  listed once; a file whose `file` is null, which Nexus Mods no longer offers, is counted. The recording script records
  a collection with `-Collections -CollectionSlug <slug>`.
- **Application reads a collection as a list** (`IModLists.ReadCollectionAsync`): a mod for each mod its files are of,
  in the order it first lists them, to watch, with those files; the list's title is the collection's name. `ModList`
  gains `Elsewhere` and `FilesUnavailable`, which only a collection fills; the list file doesn't hold them.
- From there it's a list like any other: the same plan, dialog, command and tool. What it needs from elsewhere is
  named, with each page to open, and never downloaded. Its install steps, load order and the files bundled in the
  collection's own archive are never used: the collection's archive isn't downloaded.
- The Nexus source's link reader still refuses a collection link as a file to download, now saying to import it as a
  list instead.

### Where it shows

- **Desktop app:** the Library's **Export list…** (the library, or on a game's page that game's mods or the selected
  ones) and **Import list…** › **From a list file…** or **From a collection link…**, which open one dialog: what
  importing would do, the choices (download the files, with the optional ones and the newest main file for a missing
  one; or only watch the mods), what a collection needs from elsewhere with **Open page**, then the import with its
  progress and **Stop**, and what it did and couldn't do.
- **Command line:** `library export [--to <file>] [--game <game>]` and
  `library import <file-or-link> [--watch-only] [--optional] [--newest-for-missing] [--yes]`, which says what it would
  do until given `--yes`. It's refused while another process runs the downloads, as `download` is.
- **MCP server:** `library_export` and `library_import`, which takes `list` (the JSON text) or `collection` (a link),
  and `preview`.

## Consequences

- A library can be backed up, moved and shared as a small file, and rebuilt from it here or elsewhere, one site
  request per mod and a download's for each file.
- A collection's files can be downloaded and their updates followed, with nothing installed. Users who want the
  collection installed still use a mod manager; ModShelf keeps their files.
- New public types in Application (`IModLists` and its records, in `Lists/`) and in Sources.Abstractions
  (`IModCollections`, `CollectionRevision`, `CollectionFile`, `CollectionResource`, in `Collections/`), approved. No
  database change and no new package (`System.Text.Json`).
- Log events 2300–2302 are new, in Application's new block for mod lists, and 980 in Presentation's.
- The MCP server has 28 tools.

## Testing

- Application: the format written and read back, its refusals (not a list, a newer version, a mod without its IDs),
  lenient reading, duplicates merged; export's scope and order, archived versions left out; the plan's counts; import
  watching, keeping a mod's own choices, queuing each file once, files gone, the newest main file instead, sites
  failing or unknown, progress, and the log. A collection read as a list, a link no site reads, and a site's refusal.
- Sources.Nexus: collection links read and refused; the query's variables; a revision's files with duplicates and files
  no longer offered, sections, pinned versions and authors; what it needs from elsewhere; a collection Nexus Mods
  doesn't have, and a refused query. The contract checks that a source with collections reads none from its own mod
  links or another site's. No collection is recorded yet: the answers are written by hand to the schema's shape, and
  the recording script's new option was run against a local fake of the API, its recording replayed by the source.
- Command line: export to a file and the output, a game's; the plan before `--yes`, the import, waiting for the user's
  downloads, problems and exit codes, files that aren't lists; a collection previewed and imported, and why one
  can't be read.
- MCP: the tool list, export and import back, problems, a collection previewed with what it doesn't download, and
  refusals (neither or both inputs, no such collection, a link that isn't one).
- Presentation: the dialog's facts as the choices change, the import with its options, its result and problems, Stop,
  a failure, the requests warning, a collection's elsewhere and unavailable files; the Library's export (the library, a
  game, the selection, cancelled, unwritable) and import (a list file, unreadable, too large, cancelled, a collection
  link, and each reason one can't be read).
- By hand: the manual checklists' new sections (command line 17, desktop app 19, MCP server 9), against a real Nexus
  Mods collection.
