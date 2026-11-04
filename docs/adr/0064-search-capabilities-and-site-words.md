# 64. Search capabilities and each site's words

- Status: Accepted
- Date: 2026-11-04

## Context

`SearchQuery` holds Nexus Mods' orders and filters, and a site that couldn't do one refused it (ADR 0062). The example
site showed what that meant:

- Browse offered every order, and the site refused the one the user picked.
- The CLI's `--page 2` started at `(page - 1) × page size`, so a site whose pages hold fewer than asked left mods out:
  page 2 of 100 started at the 100th when the site gave 50.
- The CLI said "most endorsed first" and listed an Endorsements column of dashes for a site without endorsements.

Nexus Mods' words were everywhere: **Track on Nexus Mods**, **Tracked mods**, "endorsements", **Most endorsed**.

The human chose (2026-11-04):

- a record each site declares, `IModSearch.Capabilities`: its orders, filters and largest page. ModShelf checks every
  search against it, and Browse offers only those;
- a few words from each site, `IModSource.Words`, put in ModShelf's own phrases. Three of Nexus Mods' texts change so
  the phrases suit any site's words;
- including adult mods on a site without adult content is accepted, since it adds nothing. The other filters a site
  lacks are refused.

## Decision

- **Sources.Abstractions** (a public change):
  - `SearchOrder` is a sort and its direction, with `BothWays(sort)`.
  - `SearchFilters` are flags: `Text`, `Author`, `Category`, `UpdatedSince` and `AdultContent`.
  - `SearchCapabilities(orders, filters, maxCount)` lists the orders (the site's own first, repeats left out), the
    filters, and the largest page (1 to 100). It has `Has(order)`, `Has(sort)` and `Has(filters)`.
  - `IModSearch.Capabilities` is new.
  - `SiteWords` holds `Follow`, `Unfollow`, `Followed`, `Approval` and `Approvals`. Each is lowercase and trimmed, or
    null where the site has no such thing. It also has `None`, `CanFollow` and `CountsApproval`.
  - `IModSource.Words` is new.
  - `SearchSort.Relevance` without words is the site's own order.
  - `SearchSort.Endorsements` and `ModInfo.Endorsements` keep their names: in code, in the CLI's `--sort Endorsements`
    and in the MCP server's `endorsements`. Each means whatever the site counts as approval. Whether to rename them
    is step 7's question.
- **Nexus Mods:**
  - its search has every order either way round (relevance first), every filter, and 100 mods a page, as before;
  - its words, from its `Strings.resx`, are "track", "untrack", "tracked", "endorsement" and "endorsements".
  - Direct links have no words.
- **Application:** `ModLookup.SearchAsync` checks each search against the site's capabilities before sending it.
  - It checks the filters first, then the order, then the page size.
  - A refusal is an `ArgumentException` naming the site and what it can do, such as "Example Mods can't search by
    author. It can search by words and by when mods were updated."
  - No signature changes.
- **Browse** offers what the chosen game's site can do:
  - its orders, with approval in its words ("Most endorsements", "Most likes");
  - the update window, author box, category box and adult switch, only where the site has each;
  - page sizes up to its largest.
  - Changing game keeps each choice the new site has. A choice it lacks becomes the site's own.
  - An author typed for another site's game stays in the box, but isn't sent.
  - The game's categories are read only when its site searches by them.
  - The **Recently added** and **Recently updated** rows show only when the site has those orders. When the site's
    search isn't known, they show.
  - The button that lists followed mods shows only for a site that follows mods, in its words.
  - A game's saved order or page size that its site lacks, such as one set by hand, starts on the site's own.
- **Mod pages:**
  - The follow button reads **Track on Nexus Mods**. The note after it reads "Tracked on Nexus Mods." or "No longer
    tracked on Nexus Mods.", and a failure reads "couldn't change your tracked mods". All of these are in the site's
    words.
  - The approval count reads "87 endorsements". It shows only where the site has words for it.
- **A game's settings page:** "How Browse starts" offers only the site's choices. A stored choice the site lacks shows
  Browse's own, and isn't changed until the user changes it.
- **CLI and MCP:**
  - Without `--sort`/`sort`, a search uses the site's own order. `--ascending` turns that order around.
  - Without `--page-size`/`pageSize`, a page holds 20 mods, or fewer when the site's pages hold fewer.
  - Refusals are Application's.
  - The approval column in `mod search`, and the approval line in `mod show`, are named in the site's words, or left
    out.
  - Relevance without words is described as "in the site's own order".
  - `settings set games.<site>/<game>.browse.*` refuses an order, direction, update window or page size the site
    can't do.
- **Changed wording for Nexus Mods:**
  - "You track 12 mods of Stardew Valley on Nexus Mods." is now "12 tracked mods of Stardew Valley on Nexus Mods."
    (and "1 tracked mod", "No tracked mods").
  - **Most endorsed** and **Least endorsed** are now **Most endorsements** and **Fewest endorsements**.
  - In the CLI, "most endorsed first" is now "in the site's own order" for relevance without words, and "most
    endorsements first" for `--sort Endorsements`.

## Consequences

- A new site declares what its search can do and its words, and the screens, the CLI and the MCP server follow without
  a change of their own.
- The CLI's pages follow on whole, since a page larger than the site's is refused.
- `mod_search`'s `sort` and `pageSize` now default to null, meaning the site's own. Nexus Mods answers as before.
- The contract checks that:
  - a site that follows mods gives all three words for it, and one that doesn't gives none;
  - approval comes as two words or none, all lowercase;
  - a site that sorts by approval names it.
  - With a game to search (`SearchableGame`), each declared order lists mods in that order, each filter narrows them,
    pages are full while more mods match, and a site without adult content marks no mod as adult.
- Nexus Mods' contract class names no game to search: its recordings answer only the searches recorded, so these
  checks are skipped for it. The example site runs them.
- The CLI's `mod track` and `mod untrack`, and their messages, keep Nexus Mods' word until step 7.

## Testing

- Sources.Abstractions:
  - the capabilities, orders and words: kept, trimmed, refused;
  - the example site under the extended contract.
- Nexus Mods: its capabilities and its words.
- Application:
  - each kind of refusal, with the search never sent;
  - the order in which problems are reported;
  - a site that narrows by nothing;
  - adult mods accepted;
  - a search the site can do, sent as asked.
- CLI and MCP:
  - the site's own order and page size, and that order turned around;
  - an order the site has only one way round, refused;
  - approval in the site's words, and no column without them;
  - the example site paged whole, its refusals, and adult mods accepted there;
  - the `settings` refusals.
- Presentation:
  - what Browse offers for a site's capabilities and words, page sizes up to its largest and its own order first;
  - a narrower search than the example site's: its orders and page size only, no update windows, no Recently added
    row, and no categories read;
  - the example game's offer, and Nexus Mods' game offering everything again;
  - choices kept or replaced when the game changes;
  - an author left out of the search;
  - a saved order the site lacks;
  - another site's words on Browse and on a mod's page, and in its failures;
  - a site without words;
  - the game settings page's offer, with Browse's own as the site's own order.
- 48 deliberate bugs, all caught. Seven got through at first, and tests were added for them:
  - update windows offered for a site without them, on Browse and on a game's settings page;
  - the Recently added row shown for a site without that order;
  - categories read for a site that can't search by them;
  - "Say how Browse starts" starting on Browse's first order rather than the site's own;
  - the CLI and the MCP server starting on the site's own order, but not its own direction.

  One didn't build, for the analyzers, and was rewritten.
- By hand: `docs/manual-checklist.md` 3.5, and `docs/manual-checklist-desktop.md` 3.3a2 and 9.3.
