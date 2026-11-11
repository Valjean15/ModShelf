# 90. DEG Mods, the fourth site

- Status: Accepted
- Date: 2026-11-11

## Context

1.0.4 adds DEG Mods (`degmods.com`), following `docs/adding-a-source.md`. The human first chose BasedMods for 1.0.4, and
set it aside (2026-11-11) once its catalog turned out to hold mods glorifying real school shooters, a weapon named after
sexual violence and racial-supremacy content, with nothing marked; DEG Mods, which marks adult mods and publishes its
admins' moderation, took its place.

DEG Mods has no API of its own: its mods are **Nostr events** (NIP-01), signed JSON read over a WebSocket from relays,
an open protocol meant for any client, DEG Mods' own site among them. Nothing needs an account or a key. What its relays
and its built site (`degmods.com/assets/index-CX1jqn5A.js`) do, read 2026-11-11, has moved on from its code on GitHub
(last commit 2026-06-20):

- **Relays:** `wss://brs.degmods.com` (khatru) serves mods, mod jams and moderation tags, and refuses other kinds with
  `CLOSED`; `wss://relay.degmods.com` (strfry) serves everything, its admin's lists among it. Each sends 500 events a
  request at most, and each holds some of what the other doesn't.
- **Mods:** since 2026-08-01, kind **31142**, addressed `31142:<author>:<d>`; before, kind **30402** tagged `t=GameMod`,
  which the site shows when made before 2026-08-01, and not when it's tagged `legacy` `yes` (moved to the new kind). An
  author's mod of both kinds is one mod, the new event winning. 113 new and 815 older mods, 248 games, about 3 MB.
- **A mod:** `title`, `summary`, its page in Markdown, `image`, `g` (the game's name; older: `game`), tags (`t`; older:
  `tags`), `content-warning` (older: `nsfw` `true`), `published_at`, `deleted` `true` once deleted, and its files as
  `download` tags (older: `downloadUrls`), each JSON: its link, its SHA-256 (112 of 140), a title, a file name, a
  version. An edit replaces the event: no file has a date, and a replaced file is gone (307 of 956 mods were edited a
  day or more after publishing). 84% have one file.
- **Files:** 140 new downloads: DEG Mods' Blossom servers `brs.degmods.com` (56) and its older `bs.degmods.com` (29,
  refusing connections), catbox.moe (14), other Blossom servers (18), MediaFire, archive.org, Google Drive and a few
  others. A Blossom server names a file by its SHA-256, so any of them may have it: of the 225 files on DEG Mods' own
  servers, 106 are on others too (`blossom.primal.net` 89, `nostr.download` 14).
- **DEG Mods' download gate:** `brs.degmods.com` answers `428` with two requirements, a proof of work
  (`X-Blossom-Gate-Pow`, 17 bits) and its ad shown for a second (`X-Blossom-Gate-Ad`), tied to the client's address;
  its page sends both proofs, and the work alone is refused.
- **Moderation**, by the admin `f4bf1fb5…` (DEG Mods' own account): blocked mods (kind 30078 `blocked-mods`), blocked
  authors (kind 10000), tags hidden by default (kind 30078 `moderation-excluded-tags`: politics, religion, gore, and two
  for content sexualizing minors), and moderation tags (kind 30985) adding a content warning to a mod whose author left it
  off. The site lets its users turn these off.
- **Content:** the catalog holds mods that sexualize child characters, which the site hides by default by those tags,
  not all of them marked NSFW; and, as BasedMods, openly Nazi and antisemitic mods, unmarked and not hidden. The human
  chose (2026-11-11) to go ahead: the catalog as DEG Mods lists it, less those its moderation leaves out.
- **Games:** the admin's games database lists about 170,000 games (28 MB of CSV on Blossom), mostly Steam's catalog;
  248 have mods.

The human chose (2026-11-11): check every event's signature with **NBitcoin.Secp256k1** 4.0.4 (MIT, pure C#, no
dependencies); take files from the other Blossom servers, and **open the mod's page** when only DEG Mods' gated server
has a file, never showing its ad or doing its work; apply DEG Mods' moderation always, with no setting; and number the UI
and UX tweaks 1.0.5.

## Decision

### The source

- **`ModShelf.Sources.DegMods`**, one project, referencing `Sources.Abstractions` alone, and the package for
  signatures: `ILinkRecognizer`, `IDownloadResolver`, `IModCatalog`, `IModSearch` and `IUpdateFeed`. No account,
  quota, handoff scheme, trending (DEG Mods counts nothing), watchlist or requirements (free text). The hosts register it
  with `AddDegModsSource()`; its event IDs are 2000–2099. Its ID is `degmods`.
- **Keys:** a game is its name as DEG Mods writes it, lowercase (`fallout: new vegas`), since the site tells games apart
  by name in any case; it's shown as most of its mods spell it. A mod is its author's key and its `d` tag,
  `<author>:<d>`, the same for both kinds of event. A file is the first 16 hexadecimal digits of its SHA-256, or of its
  link's when it has none.
- **`CouldBeGameKey`:** a key alone of lowercase letters, digits and hyphens, such as `starfield`, is left to Nexus
  Mods (or a number to GameBanana and mod.io), and one with capitals isn't DEG Mods', so nothing typed before changes
  meaning; a lowercase one with anything else, such as a space, is DEG Mods'.

### The relays

- **`RelayClient`** opens a WebSocket (`ClientWebSocket`) through the factory's handler for its own client, which
  retries a connection that fails for a passing reason; the source's named client, with the app's pipeline, reads each
  answer whole within its time limit, which a WebSocket's never is. Each relay has 30 seconds to connect and to answer
  each request (`DegModsOptions.AnswerTimeout`); later is an `HttpRequestException` holding a `TimeoutException`.
- A filter with a limit is read **a page at a time**, each until the oldest event of the page before, as long as a page
  brings events not seen yet, 50 pages at most. A filter a relay refuses (`CLOSED`) brings nothing, and isn't an error.
  A message that isn't Nostr's is an invalid answer; one of another subscription, `NOTICE` and `AUTH` are passed over.
- **Every event's ID** is checked to be the SHA-256 of what it says, as NIP-01 serializes it, and **its signature** the
  author's (BIP-340); one that fails is left out, and counted in the log (2013).
- **`DegModsRelays`** asks both relays the same at once and puts their events together; one that can't be read leaves
  the other's (2020); none is the first one's failure.

### The catalog

- **`ModIndex`** reads every mod at once (new kind; older listings before 2026-08-01), with the moderation, and keeps
  them five minutes (`KeepModsFor`), for the games list and searches. A mod asked for by itself, as a mod's page, its
  files, a link or a download ask, is read afresh by its author and `d` tag, with the moderation kept five minutes.
- **Moderation** is always applied: the admin's blocked mods (either kind's address) and authors, and its hidden tags,
  with DEG Mods' defaults when its list isn't there; **mods sexualizing minors are always left out**, whatever the
  admin's list says. A mod it hides isn't listed, and asked for by itself is a 404, "DEG Mods' moderation hides this mod",
  logged without saying which (2000). A moderation tag with a content warning marks a mod for adults.
- **Games** are the names the shown mods use, each with its page, `degmods.com/game/<name>`; no pictures (the games
  database is left for later).
- **A mod:** its title, summary, author (`npub1…`, as DEG Mods names authors without a profile), its first file's
  version, its page (`degmods.com/mod/<naddr>`, of the kind it was read from), its picture as its thumbnail too, its
  times, and adult when its author or the admin said so. A mod under another game than asked is a 404.
- **Files:** the first one listed is `Main`, the others `Optional`, each made when the event was, with its SHA-256 when
  given, and **ModShelf's link** to it, the mod's page with `#file=<id>`, which opens the mod's page in a browser.
  `FilesSideBySide` is false: a mod's one file uploaded again is its update, as on Nexus Mods, and an optional file new
  since a check a new file. Nothing in Application changes.
- A file's name is the one its author gave, or its link's; a Blossom link names its file by its hash, so its name is the
  file's title with the link's extension.
- **No changelogs.**

### Links and downloads

- **Links:** a mod's page (`degmods.com/mod/<naddr>`, with `www.`, a trailing slash, a query or a fragment), its address
  as `nostr:naddr1…`, and ModShelf's link to a file. Another DEG Mods page, an address of another kind (a mod jam), or a
  file it doesn't name the right way is refused; a `nostr:` link to anything but a mod isn't DEG Mods'. A mod's page is
  its first file; a mod without files, or one its moderation hides, is refused.
- **Downloads**, read afresh from the mod's newest event:
  - a Blossom link (`<server>/<sha256>[.ext]`): its own server, unless it's DEG Mods', with `HEAD`, then each of the
    file servers DEG Mods' site lists, by the SHA-256 alone, each host once (`FileServers`); the first that has it
    (2xx or 3xx) is the ticket's, with the SHA-256 to check. **DEG Mods' own servers are never asked**
    (`GatedHosts`): when only they have the file, `HandoffRequired` opens the mod's page, where DEG Mods' site shows
    its ad and downloads it (2002);
  - catbox.moe, the Internet Archive's `/download/` links and GitHub's release downloads and raw files are the file
    itself: the ticket is the link;
  - anything else, such as MediaFire, Google Drive or Mega, is a page, which `HandoffRequired` opens (2003).
- A file's link is never logged, only its host.

### Search and updates

- **Orders:** newest first (by `published_at`), as DEG Mods lists them, and oldest; recently changed (the event's time);
  by name both ways, in any case. Relevance is the newest first. **Filters:** words, each in the mod's name in any case;
  the author, by `npub1…` or key; changed since a time; adult content, left out unless asked. No category: older listings
  have none. 50 a page.
- **Updates (`IUpdateFeed`):** the mods changed since a time, one request to each relay (`since`), the older listings
  too when it's before 2026-08-01; each mod shown, of the game, changed since. An event doesn't say what changed, so
  `FilesUpdatedAt` is null and each is checked. 28 days, as the other feeds.

## Consequences

- DEG Mods works in the desktop app, the command line and the MCP server with no change of theirs but registering it
  and naming it in their words: `degmods/fallout: new vegas` names a game.
- **A file only DEG Mods' server has opens the mod's page.** The download then waits on that page, as a free Nexus Mods
  account's does, though nothing hands it back: the user downloads it there, then cancels it in ModShelf, or rescans
  the library once the file is in the mod's folder. About half the files on DEG Mods' own servers are on another too.
  MediaFire, Drive and Mega are always so.
- **The first Browse of a run reads every mod**, about 3 MB from two relays, a few seconds; then five minutes from
  memory.
- A mod whose author replaces its main file offers the new one as its update; one whose author replaces an optional
  file offers it as a new file. The file replaced is no longer listed, so it isn't taken for an old version: downloading
  the update moves or deletes nothing, and the earlier file stays in the library until removed by hand.
- Mods hidden or blocked by DEG Mods disappear from ModShelf too, and a library mod that becomes hidden isn't found
  any more on its next check.
- The games database's covers are left for later: DEG Mods' games show their initial.
- No public interface changed.

## Testing

- **The recordings** (`Fixtures/Record-DegModsFixtures.ps1`, run 2026-11-11): both relays' NIP-11 documents and their
  answers about eleven chosen mods (Fallout: New Vegas, Street Fighter 6, Mafia, Starfield; two test posts the admin
  moderates), the admin's lists, an author's mods in pages of three, and since a time; DEG Mods' gate's `428` (its
  challenges written as REDACTED, since they hold the client's address), a mirror's `HEAD`, its first kilobyte through
  a redirect, every mirror's `404`, catbox.moe's file and a missing one, and archive.org's redirect. No other mod of the
  catalog, and nothing its moderation hides, is in the repo: the script stops on one. Mods that are hidden, blocked,
  adult or deleted, and the made-up game the contract searches (60 mods, six adult), are made in the tests, signed with
  test keys.
- **A relay here:** WireMock.Net's WebSockets answer each REQ from the recorded events and the tests' own, as NIP-01
  says, with each relay's refusals; the source's requests for files go to the recorded answers whatever their host.
- The contract (`SourceContractTests<T>`), and the source's own tests: events read and checked, signatures and IDs,
  NIP-19 against values worked out apart, links, files, moderation, the index's keeping and reading afresh, paging,
  refusals, timeouts, a relay down, searches in each order and filter, downloads from each kind of host, the feed and its
  reach, registration.
- 124 deliberate bugs, 98 caught on the first pass. Eight the compiler refused; redefined, all were caught, one after a
  test of a relay that sends another author's mod though the filter names one. One changed nothing: a signature of no
  characters, which the check for 128 already refuses. Three led to simpler code, and their new forms were caught: a
  mask on bits never read, the always-hidden tags both added to the hidden ones and checked apart, and a refused
  filter's answer kept apart from an empty one though both were taken alike. The rest led to tests for an event signed
  but carrying another ID, a message larger than any event, the app named to the relay, Bech32's padding, an npub or an
  address under another prefix, an address with two `d` tags or a short kind, a game's page named by an address, file
  servers' links, mods ordered by when they were published against when they changed, DEG Mods' own server among the
  file servers, a change earlier in the second asked, and both apps registering every site.
