# 107. CurseForge, the sixth site

## Context

CurseForge is where most Minecraft mods are, and many for The Sims 4, World of Warcraft and other games. The human
approved, for 1.1.0 (feature 10 of the plan), a CurseForge source, `ModShelf.Sources.CurseForge`.

What the research found:

- **The API** is CurseForge's Core API (`api.curseforge.com`, documented at docs.curseforge.com): games, their
  categories, mods by ID or by search, a mod's files a page at a time, a file, several mods at once, and a file's
  download link. It answers only with an API key, sent as `x-api-key`; without one, or with one it doesn't take, it
  answers 403 with nothing in it. It never says 401. Its CDN keeps answers apart for each key: checked from the human's
  computer, an answer to a key never went to a request without it, nor a refusal to a request with one. A key just made
  was refused at first, then taken for the list of games, and only some minutes later for searches.
- **The terms** say an API key is personal and may not be shared with anyone. So ModShelf ships no key: each user makes
  their own on the CurseForge for Studios console (console.curseforge.com, **API keys**) and links it, as they link
  Nexus Mods' and mod.io's keys. The human chose this over a key built into releases.
- **Networks:** CurseForge refused every request from ModShelf's development workspaces, data-centre networks behind
  proxies, with a key that works from a home connection. Its answers were recorded on the human's computer.
- **Downloads:** a file's `downloadUrl` is its link on CurseForge's CDN, with its SHA-1 and MD5. An author can let no
  app download their mod's files (`allowModDistribution`); then the link is null, and the file is downloaded on its
  page on CurseForge's website.
- **Files:** a mod's files are builds, each for the game versions and loaders it lists, such as `1.20.1` and `Fabric`,
  side by side: a mod has a build for each. Each is a release, a beta or an alpha, and CurseForge withdraws some
  (archived, deprecated). Nothing says which file replaced which. Popular Minecraft mods have thousands.
- **Links:** `https://www.curseforge.com/<game>/<class>/<mod>`, with `/files/<file>` or `/download/<file>` for a file:
  slugs for the game, the class (such as `mc-mods`) and the mod, and a number for the file.
- **Following:** CurseForge's API gives apps no way to read or follow a user's mods.
- **Limits:** none published.

## Decision

### The capabilities

`CurseForgeSource` implements `ILinkRecognizer`, `IDownloadResolver`, `IModCatalog`, `IModSearch`,
`IModRequirements` and `IAccountLinker`. It has no `IUpdateFeed`, so each watched mod is asked for its files each check;
no `ITrendingMods`, `IGameSearch`, watchlist, endorsements, lookup by hash or quota. Its `Words` are none: ModShelf
shows no approval of CurseForge's.

### IDs

- `SourceId` is `curseforge`.
- A game is named by its slug, such as `curseforge/minecraft`: CurseForge's own permanent key, readable in folder names.
  Slugs look like Nexus Mods' keys, so `CouldBeGameKey` says no, as Thunderstore's does: a key alone stays Nexus Mods',
  and a CurseForge game always needs its site.
- A mod and a file are CurseForge's numbers.

The games are read once a run, every page of them, and a game's classes once for each game asked about.

### Accounts

One strategy, `ApiKey`: the challenge opens the console's API keys page, and the key pasted is taken when it's 30 to 200
characters without spaces (CurseForge's are 60, `$2a$10$…`). It's sent as `x-api-key`. Linking asks CurseForge for a game
with it, and the account is called "CurseForge API key", since a key doesn't say whose it is. A 403 is the key refused,
so the app asks for it to be linked again; a key can't be refreshed.

### Catalog

- A game's classes, such as Minecraft's Mods, Resource Packs and Modpacks, are its categories, and a mod's class is its
  category, so a search by a mod's category finds mods like it. CurseForge's finer categories, such as "Map and
  Information", aren't read.
- A mod: its name, summary, first author, page, logo and thumbnail, downloads, class, when it was modified and
  released; one CurseForge rejected or deleted isn't available.
- **Files:** read 50 a page, the newest first, up to 10 pages (`CurseForgeOptions.MaxFilePages`), so a mod with
  thousands of builds costs 10 requests at most a check; the log says when there were more. A release is `Main`, a beta
  or an alpha `Optional`, and one unavailable, archived or deprecated `Archived`. `FilesSideBySide` is true, and each
  file is `ReplacedBy` the next newer build of its kind (release, beta or alpha) for the same game versions and
  loaders, never a withdrawn one: an update for a 1.20.1 Fabric build is a newer 1.20.1 Fabric build, not a 1.21 one.
  The version label is left empty: CurseForge gives files a display name, not a version.
- **Changelogs** aren't read in 1.1.0: CurseForge keeps one per file, as a web page, which would cost a request a file.

### Search

By words and by class (`SearchFilters.Text | Category`), 50 a page at most, in CurseForge's orders: its popularity
first (as `Relevance`), then downloads, when updated, when released and by name, each both ways. CurseForge's search
reaches its 10,000th mod at most, so the total is capped there and a page beyond it is empty, asking nothing.

### Links, downloads and requirements

- A link names a mod by slugs, so recognizing one asks CurseForge for the mod with that slug in the game's class (the
  games and classes being read once). A mod's link is its main file; a file's link that file.
- A file resolves to its `downloadUrl`, checked by its SHA-1, or its MD5 without one. A file without a link is
  `HandoffRequired`, its download page on the website, and the download waits for the browser's file, taken from its
  downloads folder, as for DEG Mods' files elsewhere.
- Requirements are the mods the main file needs (`relationType` 3), named in one request for all of them; optional,
  embedded and incompatible mods aren't.

## Consequences

- Each user needs a CurseForge account and an API key of their own. Settings › Accounts lists CurseForge under "Needs an
  account", with the console's page to make one.
- A watched CurseForge mod costs 2 requests or more a check (the mod and its files). Popular Minecraft mods cost up to 11.
- Updates are found only between builds for the same game versions: a mod's first build for a new game version is a new
  file, not an update.
- Files whose author lets no app download them work only with the browser, as on DEG Mods.
- A key just made can be refused for some minutes, by searches longer than the rest: ModShelf says the account was
  refused, and trying again later works, with no need to link it again.

## Testing

- `CurseForgeLinkTests`: the links taken and refused.
- `CurseForgeFilesTests`: updates only between builds of a kind for the same game versions, never to a withdrawn file;
  categories; hashes.
- `CurseForgeSourceScriptedTests`, against answers the tests write: games read once a run, every page; a game's classes;
  a mod, and one of another game refused; files a page at a time up to the most read; the search asked as written, and
  what it refuses; links to mods and files, and links to nothing; download links and download pages; requirements
  named in one request; a key refused; linking a key.
- `CurseForgeSourceTests` derives from `SourceContractTests<CurseForgeSource>`, over the answers
  `Fixtures/Record-CurseForgeFixtures.ps1` recorded with the human's key: Minecraft and Mod Menu, its files, its main
  file and the mods it needs, two pages of a search, searched again in each order and by each filter over the mods
  recorded, as mod.io's tests do, the mod by its slug, a mod that doesn't exist, and a request for a game without a key
  and one for a changelog with a made-up key. CurseForge once answered a game's classes without a key, so the requests
  without one ask for what it's known to refuse. The
  script takes out the fields the source doesn't read, such as each mod's `latestFilesIndexes`, so the files stay
  small.
