# 62. The structure every source follows

- Status: Accepted
- Date: 2026-11-04

## Context

From 1.0.1, each patch release adds one mod site (decided 2026-11-03). Step 1 of the 1.0.0 plan was to check what of
Nexus Mods had leaked out of its source, to write down what a source is, and to prove the design with a second site
before a real one arrives.

The audit read the 67 files outside `ModShelf.Sources.Nexus` that name Nexus Mods or `nxm://`, then looked for code
shaped by Nexus Mods without naming it:

- **Examples, which are fine:** nearly every mention is a comment or help text that says "such as Nexus Mods",
  `nxm://` or `stardewvalley`. The CLI's and the MCP server's wording is reviewed in step 7. The database keys mods by
  site, game and mod, and the Library and Updates group games by site and key.
- **To move into the Nexus source:** nothing. What only Nexus Mods does, such as update links back in time, is there
  (ADR 0059). The rescan allows a file's size to be a kilobyte out, which Nexus Mods needs and no site is hurt by, since
  files are matched by name first.
- **To make general:**
  1. Search: `SearchQuery` and `SearchSort` are Nexus Mods' orders and filters (endorsements, unique downloads, author,
     category, adult content), Browse offers all of them, its rows assume every site orders by newest and updated, and
     the page sizes are 20, 50 and 100. Step 3.
  2. Words: "Track on Nexus Mods", "Tracked mods", "endorsements" and "unique downloads" are Nexus Mods' words for
     every site. Other sites follow, subscribe or like.
  3. Game settings: `games.<game>` in `settings.json`, the hidden games and the Settings screen's game list are keyed by
     the game's key alone, so two sites with a game of the same key would share them, though games are kept apart per
     site (decided 2026-11-03).
  4. Updates: they're found among main files, and new optional ones. A site whose files have no category (`Unknown`)
     would never show one.
  5. HTTP: what every site's API client needs (retries that leave refusals and the rate limit alone, a time limit per
     attempt with the whole answer read inside it, no redirects) was in the Nexus source, for the next site to copy.
- **For the guide only:** game keys show in folder names and settings, so they're readable and never change; a link
  that doesn't name its game, as on GameBanana, needs a request; and on mod.io, a subscription makes games install the
  mod ([mod.io](https://docs.mod.io/unity/subscribing)), so it's never a watchlist. The implementation plan's "game
  identity map", grouping one game of two sites, is replaced by keeping them apart.

The human chose (2026-11-04): the example site as shared test files rather than a project; the HTTP setup moved to
Infrastructure now; each site's own words, proposed with step 3 or 7, a public change; and game settings keyed by site
and game in step 2.

## Decision

- **The guide, `docs/adding-a-source.md`:** research first (official API, terms, limits, accounts, downloads, what
  following does); one project referencing `Sources.Abstractions` only; the IDs that never change; each capability and
  what ModShelf does without it; how updates are found, so a site without categories lists each version as a main file
  (item 4, by rule rather than code); links, handoffs, accounts, HTTP and errors; pictures; the site's keys for
  ModShelf; tests and recordings; what a site adds beside its project; and stopping to change the guide when a site
  doesn't fit. Each of 1.0.1 and on follows it.
- **HTTP (item 5):** `AddSourceHttpClients(SourceHttpOptions?)` gives every client named by
  `SourceHttpClients.NameFor` the linked account, then retries (3, from a second, doubling, with jitter; not `429`, and
  not waiting out `Retry-After`), a time limit per attempt (30 seconds) with the whole answer read inside it, and no
  redirects, whichever handler the host chose. A request whose last attempt ran out is an `HttpRequestException`
  without a status holding a `TimeoutException`, so a source never sees the retry library's exception. The handlers a
  source adds sit inside the retries. `AddNexusSource()` keeps its address, its headers, no overall timeout and its
  rate-limit handler; `NexusOptions` loses `MaxRetryAttempts`, `RetryDelay` and `AttemptTimeout`, which are now
  `SourceHttpOptions`'. Nexus Mods' client behaves as before.
- **The example site, `tests/Shared/ExampleSite`:** `example`, "Example Mods". Its game keys have hyphens
  (`stardew-valley`, which Nexus Mods has too, and `pixel-farm`); its mod IDs are letters; each version is a main file,
  with no update chains, exact sizes and SHA-512; files download directly, with no account and no handoff; its search
  has five orders, refuses the others and a search by author or category, and gives at most 50 a page; it has no feed,
  watchlist, trending mods, requirements, quota, changelogs or pictures. It passes `SourceContractTests<T>`, and runs
  through the CLI and the Library, Updates and Browse screens beside Nexus Mods. Every later step keeps it passing.
- Items 1 and 2 go to step 3 (and 7 for the CLI and the MCP server), item 3 to step 2.

## Consequences

- A new site's project holds only what's its own; the next one doesn't copy Nexus Mods' HTTP setup.
- The Nexus source's tests set it up as the hosts do, through Infrastructure, so they test what users run.
- What the example site showed, for later steps:
  - a site that gives fewer mods than a page asks for leaves some unreachable: the CLI's page 2 of 100 starts at the
    100th mod, though the site gave 50 (step 3, page sizes);
  - Browse and the CLI offer orders the site refuses; the refusal is said, not ignored, and the list kept (step 3);
  - the CLI's search says "most endorsed first" and has an Endorsements column of dashes for a site without
    endorsements (steps 3 and 7);
  - the two Stardew Valleys are two games in the Library, Updates and Browse, but only Browse names their sites
    (steps 2 and 9).
- The library, the update check (without a feed, categories or chains), the rescan, `mod show` and the mod page work
  for the example site as they are.

## Testing

- Infrastructure: retries of passing failures and not of refusals or `429`, `Retry-After` not waited out, retries
  waiting on the registered clock, a retry carrying the account as the first attempt did, a failure that lasts reported
  after the retries, an attempt without an answer and one that stalls part-way reported as the site not answering, the
  whole answer read, no redirects with either handler, other clients left alone, a second call changing nothing, and
  options that can't work refused.
- Nexus Mods: its tests, now through the app's setup, unchanged but for the timeout's inner exception, now checked
  further; its options' checks of retries and time limits moved to Infrastructure; its client without an overall time
  limit, which no test checked before.
- The example site: the contract, with the checks of the capabilities it doesn't have skipped; the CLI end to end
  (download into `example/stardew-valley/Better Crops (k7Qx2p)`, a new version found by `library check`, a rescan,
  search, refusals, `mod show`); Browse (both sites' games, rows without trending, search, a refused order, a mod page
  from a link), the Library (one game of two sites kept apart) and Updates (each under its game; no changelog).
- 20 deliberate bugs in the shared setup and the Nexus source's part of it, all caught. Three got through at first, and
  tests were added for them: retries on the system's clock rather than the registered one, the account added inside
  the retries rather than outside, and Nexus Mods' client with an overall time limit.
