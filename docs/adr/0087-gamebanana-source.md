# 87. GameBanana, the second site

- Status: Accepted
- Date: 2026-11-10

## Context

1.0.2 adds GameBanana (`gamebanana.com`), the second mod site, following `docs/adding-a-source.md`. Its API, `apiv11`,
is the JSON its own pages read: no key, no account, and nothing to ask GameBanana for. The human chose (2026-11-03) to
use it as it is, without asking GameBanana to list ModShelf among the mod managers its 1-Click Install button offers.

What the recordings showed (`Fixtures/Record-GameBananaFixtures.ps1`, Persona 5 Royal (PC), game 16951, and P5R Female
Protagonist, mod 440088):

- **Pages** hold 50 at most. A search by words always answers 15 a page, and its count of results can change from one
  page to the next.
- **Orders:** a game's mods newest, oldest, latest modified, latest updated, most liked, most downloaded, A to Z and
  Z to A; a search by words by best match, popularity, date and update date. **Filters:** the game, a category (a top
  category takes its subcategories), a submitter by member ID, and content ratings, only to take rated mods in, never to
  leave them out. Words can't be searched with an author or a category, and nothing searches by date.
- **Adult mods:** each mod's content ratings, and how the site shows it, `_sInitialVisibility`: `show`, `warn` (behind
  a warning) or `hide` (hidden until asked).
- **Files** sit side by side: a mod's variants and add-ons, each uploaded when its author did, none marked main, and
  nothing saying which replaced which. An author can move older versions apart (`_aArchivedFiles`), each with its
  version but not its successor. Each file has an exact size, a date and an MD5; one GameBanana couldn't read inside
  (`_bHasContents` false) is a mod manager's small release file. A file's link, `gamebanana.com/dl/<file>`, leads (302)
  to `files.gamebanana.com`.
- **1-click links** name the file alone, `gamebanana.com/mmdl/<file>`, or, from the button, behind a mod manager's
  scheme with the mod after it: `<scheme>:https://gamebanana.com/mmdl/<file>,Mod,<mod>`. No call found gives a file's
  mod from its ID.
- **A game or mod that doesn't exist** is a 404 at first, then a **200 with the same `_sErrorCode`** once GameBanana
  keeps the answer, for up to 10 minutes; profiles come as `text/html`.
- **The games list** is 4,835 games, 97 pages of 50. No rate-limit headers.
- Each mod's updates: a version, a date and its changes, by kind.

The contract (`SourceContractTests<T>`) expected a site that marks adult mods to leave them out unless asked, which
GameBanana can't; and the update check took a newer main file for an older one's update by date, which, with files side
by side, would offer a mod's "tan" variant as the update of its "pale" one. The human chose (2026-11-10):

- **Adult mods:** mark, blur and hide them in ModShelf.
- **Updates:** a file's archived versions or a new file, never its siblings.
- **A game's key alone** (`--game 16951`) picks the site by the key's shape, and the agent's instructions say which site
  each mod is from and where to search.

## Decision

### The source

- **`ModShelf.Sources.GameBanana`**, one project, referencing `Sources.Abstractions` alone: `ILinkRecognizer`,
  `IDownloadResolver`, `IModCatalog`, `IModSearch` and `ITrendingMods`. No `IAccountLinker`, `IWatchlistSync` or
  handoff: nothing on GameBanana needs an account, and its 1-Click Install needs it to list ModShelf. No `IUpdateFeed`:
  nothing lists a game's mods changed since a time, so each mod is checked on its own. No `IModRequirements`: the
  profile's requirements are free text. The hosts register it with `AddGameBananaSource()`; its event IDs are
  1800–1899.
- **Keys:** a game is its GameBanana number, such as `16951`, and a mod its number, such as `440088`. Its ID is
  `gamebanana`, stored with every job, so it never changes.
- **`GameBananaApiClient`** sends at most 4 requests at once (`GameBananaOptions.MaxRequestsAtOnce`), through the
  source's HTTP client, whose pipeline retries what fails for a passing reason and follows no redirect, with
  `ModShelf/<version>` as its user agent. It reads the body whatever the status or the content type: `_sErrorCode`
  `NO_SUCH_RECORD` is a 404, any other code the status given, or 400; a 429 is a 429; anything that isn't JSON is an
  invalid answer.

### Links

- A mod's page (`gamebanana.com/mods/<mod>`) and its download page (`…/mods/download/<mod>`) are the mod: its archive
  uploaded last, or its file uploaded last when it has no archive.
- A 1-click link that names its mod (`gamebanana.com/mmdl/<file>,Mod,<mod>`), as it is or behind any scheme but
  `file`, `javascript`, `mailto`, `data`, `vbscript` and `about`, is that file.
- A `dl/<file>` or `mmdl/<file>` link without its mod is refused, saying to copy the mod's page link; any other
  GameBanana link, such as a game's page, is refused as not a mod's.
- Each is checked against the mod's profile, which names its game.
- **A file's link** is its 1-click link, naming its mod: GameBanana gives files no page, and the app downloads a file it
  lists by its link, on a mod's page, from the Updates screen, and through the command line and the MCP server.
  Opened in a browser, it downloads the file. The contract now asks every file of a source that downloads for a link.

### Files and updates

- **`IModCatalog.FilesSideBySide`**, a default member, false: a catalog that lists a mod's files side by side says so.
  GameBanana's is true. The update check then never takes a newer main file for an older one's update by date: a
  file's update comes only from its chain, and the main files uploaded since a check are new files, as optional ones
  are. The mod page and the Library call such a file "New file", and `library updates` "(new file)"; a main file of a
  mod that's only watched, or has only archived versions, is still its update, as on Nexus Mods.
- **Categories:** each current file GameBanana read inside is `Main`; one it couldn't is `Miscellaneous`; each archived
  file is `OldVersion`, with `ReplacedBy` the newest current archive uploaded after it whose name, its version left
  out, is the same. A name's version is left out by keeping its letters alone, lowercase, without its extension or the
  five hexadecimal digits GameBanana adds to a name it had already: `p5r_female_protagonist1_2_27.7z` is
  `prfemaleprotagonist`. An archived file whose name matches none has no successor, and isn't an update of anything.
- **Downloads** need no account. The link is the file's `_sDownloadUrl` when it's on GameBanana, or `gamebanana.com/dl/
  <file>` otherwise, read afresh for each download, never kept; the file transfer follows its redirect to
  `files.gamebanana.com`. The MD5 is checked.
- **Changelogs** are the mod's latest 50 updates, newest first, each its version (or its name) and its changes.

### Search

- **Capabilities:** best match first, as `Relevance`, GameBanana's own order for words, which without words is the
  newest, its own order for a game's mods; then newest first, and oldest; recently updated
  (`Generic_LatestModified`); most liked as `Endorsements`; most downloaded; and by name both ways. Filters: words,
  author and category; 50 a page at most. A game's mods in a list don't say how often they were downloaded.
- **Words** are searched with GameBanana's search, 15 a page: a page of ModShelf's takes the pages of GameBanana's it
  covers, and the count is the first one's. Words with an author or a category, or in an order the search lacks (oldest,
  by name), are refused, saying what it can.
- **A game's mods** are GameBanana's pages of the page's size; a page starting part-way into one reads the next too.
  An author is found by GameBanana's member search, by the exact name in any case; none is an empty page. A category is
  one of the game's top categories, by name in any case, read with the game; a name the game lacks is refused.
- **A mod GameBanana lists under another game** is left out, and logged (1801).
- **Trending** is the game's top submissions (`Game/<id>/TopSubs`), its mods only, each once.
- **Games:** every game, read once a run, 97 pages a few at a time (1800).

### Adult mods

- **`SearchCapabilities.MarksAdultContent`**: the site marks adult mods. It's true whenever the site can filter by
  `AdultContent`; GameBanana, which can't, sets it on its own.
- **`SearchPage.LeftOut`**, how many of a page's mods were left out after the site answered; **`NextOffset`** is where
  the next page starts, past them, and `HasMore` counts them.
- **`ModLookup.SearchAsync`** leaves out the adult mods of a site that marks them but can't leave them out, unless the
  search asks for them. Browse offers the adult content filter for such a site, and starts at `NextOffset`. `mod search`
  says how many it left out, and to add `--adult`; `mod_search` says it in `leftOut`. A mod GameBanana shows behind a
  warning or hides is adult; its pictures are blurred or hidden as `pictures.adult` says.
- **The contract:** a site that marks adult mods without the filter skips "marks no mod adult without adult content",
  and a search's adult mods are checked only on a site with the filter.

### A game's key alone

- **`IModCatalog.CouldBeGameKey(key)`**, a default member, true: whether a key could name one of the site's games.
  Nexus Mods' are names with a letter, as its links have them (`stardewvalley`); GameBanana's numbers.
- **`GameSites.ThatCouldHave`**, in the CLI: the sites whose keys could be the key, or every site when none could. A
  game named by its key alone in `settings set` (`games.<game>`, `library.folders.<game>`), and `mod search` and
  `mod_search` without a site, take the site that way; two that could are still asked which.
- **The MCP server's instructions** name the sites, `nexus` and `gamebanana`, say every result names its site, give each
  site's keys, and say to search each site a game is on. The tools' descriptions of `game` and `source` say the same.

## Consequences

- GameBanana works in the desktop app, the command line and the MCP server with no change of theirs but those
  above: Browse, the Library, Updates and Downloads take it as they take Nexus Mods, and the sites' chips and the
  Library's site filter appear once its mods are in the library (ADR 0069).
- A mod's variants and add-ons are new files, offered once each, never updates of each other. An author who uploads
  a new version beside the old one, without archiving it, offers it as a new file; once the old one is archived, the
  new one is its update.
- Adult mods cost a search the room they took: a page can list fewer than its size, and Load more starts after them.
- **Listing every game is slow:** 97 requests, once a run, each 2 to 6 seconds while GameBanana hasn't kept the page,
  0.2 once it has. Read 4 at a time from here (2026-11-10), the whole list took 57 seconds. Browse, quick search and Add
  a game read every site's games before showing any, so the first time Browse opens in a run, Nexus Mods' games wait
  for GameBanana's too. Left open for the human before 1.0.2 is released: reading each site's games apart, keeping the
  list between runs, or searching GameBanana's games by name (`Util/Game/NameMatch`, one request of 0.3 seconds), which
  needs a capability of its own.
- `mod updates` (and `IUpdateFeed`) isn't offered for GameBanana; `library check` asks for each watched mod's profile,
  4 at a time.
- A pasted 1-click link without its mod can't be used; the button's links, which name it, can.
- The Library's label for a main file that replaces none of a mod's current files is "New file" where it was
  "Update": on Nexus Mods, where only a mod without current files has one, nothing changes.

## Testing

- 44 recorded answers, served by WireMock, and the contract (`SourceContractTests<T>`): 176 tests, 10 of the contract's
  skipped for what GameBanana doesn't do (accounts, handoffs, feeds, watchlists, requirements).
- Links of every kind, refused ones, and schemes never fetched; the files' categories, successors, names, hashes and
  links; each order and filter as GameBanana's query, words over several pages of 15, a page part-way into one, a
  missing author and category, refusals; a missing mod or game as a 404, a 200 with an error, and HTML; 429;
  trending; the games list across its pages.
- The update check side by side; the labels on the mod page, the Library and the CLI; adult mods left out, counted and
  paged past, in Application, Presentation, the CLI and the MCP server; a game's key alone in the CLI and Nexus Mods'
  keys; the contract's new check that every file has a link, which Nexus Mods passes too.
- Tried from the command line against GameBanana itself (2026-11-10): `mod show`, searches in each way, `mod games`,
  downloads by every kind of link into a folder and the library, with their MD5 checked, `library check`, and the
  refusals. That found the files without links and searches by words newest first, both fixed above, and timed the
  games list.
- 62 deliberate bugs, 61 caught. The one left drops the check that a link behind a mod manager's scheme is
  GameBanana's, which the link is checked for again after.
