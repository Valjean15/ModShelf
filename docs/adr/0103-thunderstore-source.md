# 103. Thunderstore, the fifth site

## Context

Thunderstore (`thunderstore.io`) hosts the mods of nearly 400 games, each a community, such as Lethal Company, R.E.P.O.
and Risk of Rain 2. Its mods are packages: a zip with a manifest, uploaded by a team, in versions. It needs no account
to read or download, and mod managers such as r2modman and Gale use its API.

The human approved, for 1.0.9 (feature 6 of the plan), a new project, `ModShelf.Sources.Thunderstore`, and ModShelf
taking over Thunderstore's own link scheme, `ror2mm://`, once the user says so, as it does Nexus Mods' `nxm://`.

## Research

- **The API:** Thunderstore publishes its API's schema at `thunderstore.io/api/docs/`, generated from its open-source
  server (`github.com/thunderstore-io/Thunderstore`), which this ADR's facts were read from. It has no API terms, asks
  apps to register for nothing and gives no keys; it publishes no rate limits.
- **A community's packages:** `/c/{community}/api/v1/package/`, the plan's endpoint, is deprecated in favour of
  `/c/{community}/api/v1/package-listing-index/`, which mod managers read. It answers `302` to a blob on Thunderstore's
  CDN (`ccdn.thunderstore.io`): gzip-compressed JSON, an array of the URLs of the listing's chunks, each a gzip-compressed
  JSON array of packages in the old endpoint's format, at most 14 MB unpacked. The index is rebuilt once an hour, at the
  hour; it takes `If-Modified-Since` and answers `304` when it hasn't changed since. A chunk's URL names its SHA-256, so
  a chunk never changes. The largest community, Lethal Company, is 26 chunks, about 38 MB to download and 350 MB of JSON;
  R.E.P.O. is 3, about 4 MB; most are one small chunk.
- **A package** in the listing: its name, team (`owner`), `full_name` (`Team-Name`), page, creation and update times,
  likes (`rating_score`), whether it's pinned, deprecated or has adult content (`has_nsfw_content`), its categories, and
  its active versions, newest first, each with its number, description, icon, dependencies (`Team-Name-1.2.3`),
  download link, downloads, upload time, website and size. No hashes.
- **Names never change:** a package's name can't be changed without making a new package, and a team's only setting is
  its donation link. A team's name can hold a hyphen (`F-tank`); a package's can't.
- **Communities:** `/api/cyberstorm/community/` lists every listed community in one answer (376), with its identifier,
  name and pictures. It's the API of Thunderstore's new website, in the same schema. `/api/cyberstorm/community/{id}/filters/`
  gives a community's categories.
- **One package by name**, wherever it's listed: `/api/experimental/package/{team}/{name}/` gives its newest version and
  the communities it's listed in. A library such as BepInEx's pack is listed in dozens.
- **Changelogs:** `/api/experimental/package/{team}/{name}/{version}/changelog/` gives a version's `CHANGELOG.md`, or
  null; authors write it as they like, mostly a heading per version.
- **Downloads:** each version's link, `thunderstore.io/package/download/{team}/{name}/{version}/`, redirects to the zip on
  the CDN and counts the download. No account, no handoff.
- **Its link scheme:** the website's **Install with Mod Manager** opens `ror2mm://v1/install/thunderstore.io/{team}/{name}/{version}/`,
  which r2modman, Thunderstore Mod Manager and Gale register. It names no community.
- **Etiquette**, from the server and the mod managers: read the listing through the index, not the deprecated endpoint;
  no more than hourly, as it's rebuilt; with `If-Modified-Since`; and name the app in the User-Agent.

## Decision

### IDs, fixed for good

| ID | Value |
|---|---|
| `SourceId` | `thunderstore` |
| `GameRef.Key` | The community's identifier, such as `lethal-company` or `repo`. |
| `ModRef.Id` | The package's full name, `Team-Name`, as dependency strings name it, such as `BepInEx-BepInExPack`. A team's name can hold a hyphen, so the name is what follows the last one. |
| `ModFileRef.Id` | The version number, such as `5.4.2305`. |

A community's identifier looks like a Nexus Mods game's key. The human chose to keep a key typed alone, on the command
line or by an agent, as Nexus Mods' (`IModCatalog.CouldBeGameKey` is false for every key): a Thunderstore game is
named with its site, as `thunderstore/repo` or `--source thunderstore`, unless the library or the settings know it
already. Following the guide's rule for keys two sites share would have made every key alone need its site, such as
`games.fallout4.hidden`.

### Capabilities

- **`IModCatalog`:** the games from the communities list, read once a run, with each one's banner as its cover (its
  background, which frames the page and is dark in its middle, when it has none) and its icon; a game's categories from its filters. A mod, its files and its requirements come from the community's
  listing (below); its changelog from the newest version's `CHANGELOG.md`.
- **The listing**, per community, kept in memory and read again at most once an hour, with `If-Modified-Since`: the index,
  then its chunks, a few at a time, each parsed as it streams, and kept small: of each package, what ModShelf shows, and
  of each version, its number, upload time, size and downloads; the dependencies of the newest only. Callers waiting for
  the same community share one read. The index's and chunks' links are followed only to Thunderstore's own hosts, over
  HTTPS. A package not in the listing, such as one removed, is a `404`: its mod has left its site (ADR 0099).
- **Files:** each version is a file, named as Thunderstore names its zips, `Team-Name-1.2.3.zip`, with its page as its
  link. The newest is the main file; each older one is an old version, replaced by the next newer one, when that one was
  uploaded after it.
- **`IModSearch`:** over the listing, leaving out deprecated packages, as the website does. Words (in the name, team
  or description), team, category, updated since, and adult content, left out unless asked. Orders: last updated (the
  website's own), newest, downloads, likes, name and size, either way round; 100 a page at most.
- **`IUpdateFeed`:** the listing's update times, so a check of a game is the listing, read at most hourly. It reaches a
  year back, though the listing holds every package.
- **`IModRequirements`:** the newest version's dependencies, each a mod of the same community, by its full name, and
  named with its team, as two teams' packages can share a name (ENVY in Hades 2's). One the community doesn't list, such as Risk of Rain 2's BepInEx pack in R.E.P.O.'s, is pointed to, as a thing off the
  site: ModShelf doesn't put another game's package in the library. Feature 5 then downloads the rest (ADR 0102).
- **`IDownloadResolver`:** the version's download link, built from its IDs, without a request.
- **`ILinkRecognizer`:** a package's page in a community (its newest version, from the listing) and a version's page;
  and, naming no community, the download link, the website's old package pages and `ror2mm://` links, whose community
  is asked of the package (`/api/experimental/package/`). A package listed in several communities can't be told apart
  that way: the link is refused, asking for its page's link, which names the game.
- **`IHandoffScheme`:** `ror2mm`. A `ror2mm://` link completes nothing, since Thunderstore never waits for one, but it's
  taken as a link to that version, so the website's **Install with Mod Manager** downloads it into the library.
- **The site's words:** likes, as Thunderstore counts them.

Not taken:

- **`IGameSearch`:** the whole list is one request.
- **`ITrendingMods`:** Thunderstore says nothing is trending; Browse has no trending row for it.
- **Accounts, watchlists and likes:** liking needs a Thunderstore account, which ModShelf can't link.
- **Modpacks** are packages like any other: their zip is downloaded, and **Download with requirements** downloads the
  mods they name.

### HTTP

`AddThunderstoreSource()` gives its client Thunderstore's address and a User-Agent naming ModShelf, its version and its
GitHub page, and sends 4 requests at once at most. The client is the app's source client, so redirects aren't followed:
the index's `302` is read and its `Location` requested. The CDN serves the blobs as `application/octet-stream`, so a
body is unpacked when it starts as gzip does, whatever its headers say.

The link scheme is registered on Windows and Linux as `nxm` is; on macOS, the app's `Info.plist` lists `ror2mm` too.

## Consequences

- Thunderstore's games and packages can be browsed, searched, downloaded into the library, watched and checked for
  updates, with their dependencies downloaded by **Download with requirements**.
- The first look at a community in a run reads its whole listing: seconds for most, but tens of seconds and about 38 MB
  for Lethal Company, in each run of the command line, which keeps nothing between runs. The desktop app then reads it
  at most hourly, mostly as a `304`. A community's listing in memory takes tens of megabytes at most.
- A key typed alone still means a Nexus Mods game; Thunderstore's games are named with their site.
- No hash is checked: Thunderstore gives none.
- No change to Domain, Application, Presentation or `Sources.Abstractions`; no schema change; no new package.
- Log events 2600–2699 are the source's.

## Testing

- `SourceContractTests<ThunderstoreSource>`, against answers recorded by `Fixtures/Record-ThunderstoreFixtures.ps1`
  from a small community, cut down, and served gzip-compressed as the CDN serves them.
- The listing: several chunks; gzip whether or not the headers say; `304`; read again only after an hour, by a
  `FakeTimeProvider`; one read for callers at once; a link to another host refused; a large listing parsed as it streams.
- Links of each kind, a package in several communities, and the IDs' edge cases, such as a team with a hyphen.
- Search in each order and filter, deprecated and adult packages left out; requirements in and out of the community;
  changelogs with and without version headings.
- By hand: the command line's and the desktop app's Thunderstore sections, and **Install with Mod Manager** after
  `links take thunderstore`.
