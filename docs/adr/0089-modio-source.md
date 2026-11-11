# 89. mod.io, the third site

- Status: Accepted
- Date: 2026-11-11

## Context

1.0.3 adds mod.io (`mod.io`), the third mod site, following `docs/adding-a-source.md` as GameBanana did in 1.0.2
(ADR 0087). Its REST API is documented, but **answers nothing without a credential**, reading included. The
implementation plan expected its emailed-code sign-in; that needs a game's own API key, and "Sign in with mod.io" needs a
client secret kept on a server, so neither is open to ModShelf. What a user can make for themselves on their account's
**API Access** page is open: an API key (32 hexadecimal characters, read only, 60 requests a minute) and personal access
tokens (OAuth 2, a year, the `read` scope, 120 a minute).

The human recorded mod.io with their own key and token (`Fixtures/Record-ModIoFixtures.ps1`, 2026-11-11; 66 answers,
the account replaced by a stand-in), Baldur's Gate 3 (game 6715) and Better Inventory UI (mod 4228735):

- **Hosts:** `api.mod.io/v1`, the user's `u-{id}.modapi.io/v1` and the game's `g-{id}.modapi.io/v1` all answered both
  credentials. An API key can't read `/me` or `/me/subscribed` (401, `error_ref` 11005).
- **Games:** 407 in all; 390 open to apps (`api_access_options` 1), 372 of them with direct downloads (2), 18 that give
  files to a token only (4).
- **A game with files per platform**, as Baldur's Gate 3: a mod read alone has an empty `modfile`, unless asked for a
  platform (`X-Modio-Platform: windows`); its `platforms` name each platform's live file. A list of mods gives the live
  file anyway. A mod's files, asked without a platform, are every file, each with what each platform said of it
  (0 pending, 1 approved, 2 denied, 3 targeted); asked for Windows, the live one only.
- **Downloads:** a file's `binary_url`, `g-6715.modapi.io/v1/games/6715/mods/{mod}/files/{file}/download`, answers 302
  to `binary.modcdn.io`, with or without a credential for a game with direct downloads. Each file has an MD5, a virus
  scan's verdict (`virus_positive`: 1 malicious, 2 may be harmful) and when its link expires.
- **Refusals:** `{"error": {"code", "error_ref", "message"}}`: 401 for a credential refused or none (11002 a key, 11005
  a token), 403 for a game closed to apps (14007), 404 for what doesn't exist (14000), 429 to slow down, with
  `retry-after`. No header says how many requests are left.
- **Events** reach back to the game's start; 552 file changes (`MODFILE_CHANGED`) of Baldur's Gate 3 in 28 days.
- **Orders:** `date_live`, `date_updated`, `downloads_total`, `downloads_today`, `subscribers_total`,
  `ratings_weighted_aggregate` and `name`, either way. **Filters**, all at once: words (`_q`), author's name, tags,
  `date_updated-min`, maturity. Nothing leaves mods with explicit content out (8 in `maturity_option`); paid mods are
  left out unless asked.
- **Dependencies** are whole mods, with their game.

The human chose (2026-11-10): **either credential, the user's choice**, in one box; **subscriptions listed, read only,
with Watch all**, never subscribing, since games install what's subscribed (ADR 0062); **games by their numbers**, such
as `modio/6715`; and building without contacting mod.io.

## Decision

### The source

- **`ModShelf.Sources.ModIo`**, one project, referencing `Sources.Abstractions` alone: `IAccountLinker`,
  `ILinkRecognizer`, `IDownloadResolver`, `IModCatalog`, `IModSearch`, `ITrendingMods`, `IUpdateFeed`,
  `IModRequirements` and `IWatchlistReader`. No `IApiQuota` (no header says what's left), `IHandoffScheme` (mod.io has
  no scheme), `IGameSearch` (390 games are four requests) or `IWatchlistSync`. The hosts register it with
  `AddModIoSource()`; its ID is `modio`, its event IDs 1900–1999. No change outside the project but its registration,
  and the command line's and the MCP server's words naming three sites.
- **Keys:** a game is its number, such as `6715`, a mod its number, a file its number. `CouldBeGameKey` takes numbers,
  so a number alone could be GameBanana's or mod.io's, and is asked which, as two sites' keys already were.
- **Addresses:** a game's requests go to its own host, `g-{game}.modapi.io/v1/`, as mod.io asks apps to; the others,
  the games, the account and its subscriptions, to `api.mod.io/v1/` (`ModIoOptions`).
- **Words:** "positive rating(s)", `ratings_positive`, as approval; "subscribed" as followed.

### The credential

- **One `ApiKey` strategy** (`ModIoAccessStrategy`), so no new `AuthKind` and no public change: the box takes an API key
  or an access token, told apart by their shape. 32 hexadecimal characters are a key, sent as the query's `api_key`;
  anything longer, of the characters a JSON Web Token has, a token, sent as a bearer token. A `Bearer` before it and the
  spaces around it are left out; anything else is refused, without quoting it, saying where to make one. Each replaces
  the other on a request. Neither refreshes: one mod.io refuses is to be linked again.
- **Whose:** a token asks `/me`, and the account is the user's name and picture, its level "Access token". A key can't
  say whose it is: it's checked by asking for a game, and the account is called "API key", its level "Without
  subscriptions". `WorksWithoutAccount` is false.
- **Never a key where a token is needed.** The app takes a 401 for linked credentials that no longer work, and asks for
  the account to be linked again; mod.io answers an API key a 401 for subscriptions, and, presumably, for a game that
  gives its files to a token only. The strategy notes on each request which credential it added, and the source notes
  the requests that need a token: the subscriptions, and a download of a game whose `api_access_options` has 4, read
  with the game once a run. **`ModIoTokenGate`**, a handler of the source's client after the app adds the credentials,
  answers such a request with an API key itself, with a 403, never sending it. Each caller says what to throw then:
  the subscriptions a 403, "mod.io lists them for an access token only: link one in place of your API key.", which the
  screens and `mod tracked` show as why they couldn't be listed; a download an `AuthenticationFailedException`, "mod.io
  gives this game's files to an access token only: …", so it waits for an account. The key stays linked.
- **The log:** the client factory's logging writes each address without its query (`?*`) and each header without its
  value, so neither the key nor the token is written; a test reads the log of a run with each.

### mod.io's pace

- **`ModIoPacer`**: at most 60 requests in any minute (`ModIoOptions.RequestsPerMinute`), each waiting its turn in
  `ModIoApiClient`, before the app's time limit on each attempt starts, so a minute's wait isn't a time-out.
- **`ModIoSlowDownHandler`**: after a 429, nothing is sent until mod.io's `retry-after` (in seconds or as a time) has
  passed, or a minute when it says 0 or nothing; each request meanwhile is answered with a 429 of its own, unsent, and
  waits for nothing (1901). The app never retries a 429. mod.io warns that asking on after one can get credentials
  revoked.

### Links and downloads

- **A mod's page**, `mod.io/g/{game}/m/{mod}` (with `www.`, a trailing slash, a page after it, a query or a fragment),
  is its live file for Windows: the game's number is looked up by its name in links once a run (`games?name_id=`), the
  mod's each time, as authors can rename it. A game or mod mod.io doesn't have is refused, the mod as perhaps renamed;
  a mod with no file for Windows too.
- **The API's links** on `api.mod.io` or a `g-`/`u-` host, `…/v1/games/{game}/mods/{mod}/files/{file}` with or without
  `/download`, are that file, read without asking; `…/mods/{mod}` is its live file. Any other mod.io link, such as a
  game's or a user's page, is refused as not a mod's.
- **A file's link** is its `binary_url` as mod.io gives it, on the game's host: mod.io's site gives files no page.
- **Downloads:** the file is read afresh, never kept. One mod.io's virus scan found malicious is refused (410, 1903);
  one it says may be harmful, such as one with a program in it, is downloaded, and the log says so (1902). Its
  `binary_url` must be on the game's API or `api.mod.io`, where the credential may go; the source asks it, with the
  credential and without following it, where it sends the download, and hands that on with the file's MD5 and when it
  expires. `binary.modcdn.io` takes no credential.

### The catalog

- **Games:** those open to apps, by name, 100 a page, the pages after the first at once, read once a run (1900). A game
  has its logo as its cover, its icon, and the tags of each group its mods can have, but those only its admins see, as
  categories.
- **A mod** is read for Windows. Its version and size are its live file's, its picture its logo, its category its first
  tag, or, found by a tag, that tag; it's available when accepted and public, adult when marked explicit.
- **Files:** a game with files per platform lists those approved for Windows, and the live one; one without, every
  file. The live file is `Main`; each other one `OldVersion`, replaced by the next one uploaded, up to the live one, and
  one uploaded after the live one by the live one: following them leads to it, as Nexus Mods' update chains do, so
  `FilesSideBySide` stays false. 100 a page, five pages at most. A file's name is its file name.
- **Changelogs:** each version's changelog, the newest first, one change a line; versions without one left out.
- **Requirements:** the mods it depends on, each with its game and page; not theirs.

### Search, trending and updates

- **Orders:** newest first, the first, as mod.io lists a game's mods; oldest; recently updated; most downloaded; by name
  both ways. `Relevance`, asked when no order is chosen, is the newest: mod.io doesn't order by how well words match,
  and it isn't offered. Approval isn't an order: mod.io sorts by a weighted rating, not the positive ratings ModShelf
  counts. 50 a page at most.
- **Filters,** at once: words, the author's name, a tag (any of the game's, in any case), and the mods updated since a
  time, in whole seconds, not before it. Mods with explicit content are marked, not left out (`MarksAdultContent`):
  ModShelf leaves them out unless asked, as GameBanana's. A mod of another game is left out.
- **Trending:** the 50 mods downloaded most today.
- **Updates (`IUpdateFeed`):** the game's `MODFILE_CHANGED` events since the time asked, from its second, 100 a page.
  Each mod is reported once, at its last change, as both its activity and its files' change. **Reach: 28 days**, as
  Nexus Mods'. More than 1,000 changes since then are beyond reach (1904): the update check then asks each mod.

### Subscriptions

- **`IWatchlistReader`**, read only, with a token: `/me/subscribed`, 100 a page, ten pages at most, each mod of its own
  game, once. Browse's **Subscribed mods**, **Watch all** and `mod tracked --source modio` list them; following or no
  longer following one is refused, saying to change them on mod.io (ADR 0088). ModShelf never subscribes.

## Consequences

- mod.io works in the desktop app, the command line and the MCP server as the other sites do, once a key or a token is
  linked; nothing of it works without one.
- An API key browses, searches and downloads from the 372 games with direct downloads; a token also lists
  subscriptions, and downloads from the 18 games that give files to a token only. A token lasts a year, and is then to
  be made and linked again.
- The box asks for "an API key", as for Nexus Mods: the linking screens have no words of a site's own, so they don't say
  a token works too. The README, the account's level ("Without subscriptions") and the refusals say it. Telling it on
  the screen needs a site's own hint, a public change, left for the human.
- A number alone names a game of GameBanana or of mod.io, so `settings set games.16951…`, `mod search --game 16951` and
  `mod_search` without a site ask which.
- 60 requests a minute: a check of many mods beyond the feed's reach, two requests each, waits its turn, minutes for a
  few hundred. A token's 120 aren't used.
- Mods for consoles or other platforms only aren't offered: ModShelf asks for Windows' files, which PC games use on
  Linux and macOS too.

## Testing

- The 66 recordings, served by WireMock, matched by path, query (the `api_key` left out) and platform; mod.io's API links
  in them made links to WireMock. What wasn't recorded word for word, a game's mods ordered or narrowed another way, the
  games list's later pages, and file changes since another time, is answered from the recorded mods, games and events,
  and anything else with a 501.
- The contract (`SourceContractTests<T>`), with a token linked and the clock at the recordings' time: 27 of its checks,
  7 skipped (handoffs, game search, watch sync, quota, approval order, adult filter).
- 258 tests in all: games and their pages, categories and refusals; a mod read for Windows, its availability and adult
  content; files per platform, the chain to the live file, pages of files; changelogs; each order and filter as
  mod.io's query, a tag as the category, adult mods marked; trending; mods' pages and API links, refused links, a
  renamed mod; downloads, their checksum, expiry, virus scan, links off the API refused, a token-only game; updates, their
  reach, pages and the cap; requirements; a token's account, a key's, refusals; subscriptions with a token, never with a
  key, which stays linked; the key and token never in the log; refusals, cut short, numbers as text; the pacer, the
  hold after a 429 in seconds and as a time, never retried, a wait not a time-out; the strategy's shapes and how each is
  sent; registration and its options.
- The command line's tests: a number alone now asks which of `gamebanana` and `modio`.
- 166 deliberate bugs, 156 caught. Three changed nothing, and the code they changed is gone: a JSON option the web's
  defaults already set, lowercasing a host `Uri` already lowercases, and a check that a mod's own file is its live one.
  Seven the compiler refused; six of them, redefined, were caught. Survivors on the first pass led to tests for a
  token after `Bearer` and more than one space, the headers sent, a live file without a size, a mod without dates, a
  tag without a name, versions ordered by date rather than ID, the live file whatever its platforms say, a checksum
  that isn't one, a file without an ID, words holding `&` and `#`, a game found among others of like names, changes
  that keep coming past ten pages, a full last page of subscriptions, and registering twice.
