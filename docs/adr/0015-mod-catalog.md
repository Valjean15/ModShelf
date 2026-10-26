# 15. The mod catalog

- Status: Accepted
- Date: 2026-10-26

## Context

Phase 3 lets the user browse a site's mods as well as download them. The plan sketches `IModCatalog` with two methods, `GetModAsync` and `GetFilesAsync`, and lists games and changelogs among what the Nexus source provides. The Library (Phase 4) and the update checker will build on it, but nothing is saved yet.

Domain already has `ModFile` and `ModFileCategory` from Phase 1, made for a file as its source describes it, and unused until now.

## Decision

### The capability

`IModCatalog` in Sources.Abstractions has four methods, one per question, so a caller asks only for what it shows. On Nexus Mods each is one request:

| Method | Returns |
|---|---|
| `GetGameAsync(GameRef)` | `GameInfo`: the game's name and page |
| `GetModAsync(ModRef)` | `ModInfo`: name, summary, author, version, page, picture, last update, whether it's available, whether it has adult content |
| `GetFilesAsync(ModRef)` | Domain's `ModFile` for every file, old and archived ones included |
| `GetChangelogsAsync(ModRef)` | `ModChangelog` per version, in the site's order |

- **Files are Domain's `ModFile`**, rather than a second record for the same thing. It gains an optional `PageUrl`: a link the source recognizes as that file, so a listed file can be downloaded from it. The category is Domain's `ModFileCategory`, whose catch-all is Unknown.
- **The long description isn't carried.** Each site writes it in its own markup (BBCode on Nexus Mods, Markdown on Modrinth, HTML on mod.io), so it comes with its format when the desktop app shows it. The summary is carried.
- **Versions are labels**, never compared, as everywhere else.
- **Links a catalog hands out must be web links**, since the app may open them in a browser. The records refuse anything else, so a source drops a link it can't trust.
- **Every method refuses another source's game or mod** with `ArgumentException`. Refusals from the site come back as `AuthenticationFailedException` and `HttpRequestException`, as for downloads.
- **The methods take `modRef`,** because `Mod` is a keyword in Visual Basic (CA1716).

The shared contract tests check that a source with a catalog:

- refuses another source's game or mod;
- describes the game and mod each of its own links points to;
- lists the linked file among the mod's files;
- gives a file page that it recognizes as the same file.

### The Nexus source

- **Game:** `v1/games/{game}.json`. The name, or the domain name if there's none, and `nexusmods_url`, or the game's page on www.
- **Mod:** `v1/games/{game}/mods/{mod}.json`. The page is `https://www.nexusmods.com/{game}/mods/{mod}`. The mod is unavailable when `available` is false or `status` isn't `published`. A hidden or removed mod without a name is "Mod {id}". A picture that isn't a web link is dropped.
- **Files:** `files.json`.
  - The section comes from `category_name` (`MAIN`, `UPDATE`, `OPTIONAL`, `OLD_VERSION`, `MISCELLANEOUS`, `ARCHIVED`). Without a name, it comes from `category_id` 1 to 5. Anything else is Unknown, including the unnamed category 6 that one SMAPI file has.
  - The size is `size_in_bytes`, or `size_kb` × 1024.
  - The file name is made safe as for downloads, or the file's ID when there's none.
  - The page is the file on the Files tab, `…/mods/{mod}?tab=files&file_id={file}`.
  - No hash: the v1 API publishes none per file.
- **Changelog:** `changelogs.json`, an object from versions to lists of changes. `{}`, `[]` and `null` all mean there's none.

### Looking a mod up from a link

- **`IModLookup` in Application** finds the source a link belongs to, the same way `ILinkDownloads` does. The rules move into one `SourceRegistry` extension: a site-specific match wins over a catch-all one, and two sites claiming a link is an error.
- It then asks the source's catalog all four questions at once, and returns a `ModPage` with the file the link points to.
- **A link nothing can look up** is an `UnsupportedLinkException` that says why: no source knows it, or its source has no catalog, such as a plain web link.
- Recognizing a link to a whole mod already asks Nexus Mods for the mod's files, to pick the newest main file. The lookup then asks again. That's one extra request per lookup, accepted to keep recognizing and describing separate.

### The CLI

`mod show <link> [--all] [--changelog]`:

- prints the mod's details, then its files by section, newest first, each with the link that downloads it;
- marks the file the link points to, since `download` gets that one;
- lists old versions, archived files and files in no section with `--all`, or when the link points to one of them; otherwise it says how many it left out;
- adds each version's changes with `--changelog`.

It doesn't download or save anything. Its error messages are the ones `download` uses for links it can't look up.

## Consequences

- The Library can save Domain's `ModFile` as the catalog returns it.
- A second source implements `IModCatalog` and passes the same contract tests. Sites with a changelog per file, such as mod.io and Modrinth, list one entry per file's version.
- What Nexus Mods means by category 6 isn't documented. Its one SMAPI file shows as "in no section" until it is.
