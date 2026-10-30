# 16. Mod search

- Status: Accepted
- Date: 2026-10-26

## Context

Phase 3 lets the user find mods, not only open ones they have links to. The plan sketches `IModSearch.SearchAsync(GameRef, SearchQuery) → SearchPage` on Nexus Mods' v2 GraphQL API.

Nexus Mods hasn't documented that API. Its staff call it a work in progress, and the schema browser at graphql.nexusmods.com can't be fetched from here. The query below follows what other mod managers send, and stays unconfirmed until a recording run captures real answers.

## Decision

### The capability

`IModSearch` in Sources.Abstractions searches one game's mods a page at a time.

- **`SearchQuery`** holds:
  - words to look for in the mods' names, or none for every mod;
  - an author;
  - whether to include adult content, which is left out unless asked for;
  - a sort: relevance, endorsements, downloads, last update or newest;
  - a direction: the most first, unless asked for the least first, such as the oldest;
  - the page's offset and size, from 1 to 100.
- **Relevance needs words to match.** Without them, a source puts the most endorsed first.
- **`SearchPage`** holds the mods as `ModInfo`, so a search result and `GetModAsync` describe a mod the same way. It also has the total the site counts, and whether more pages follow.
- **`ModInfo` gains `Downloads` and `Endorsements`**, the counts a site gives for how popular a mod is, or null when it gives none. Endorsements stand for whatever the site counts as approval: endorsements, likes or follows.
- **A source refuses another source's game** with `ArgumentException`, as the shared contract tests check.

### The Nexus source

- **The query.** `Search/SearchMods.graphql` is embedded in the assembly and posted to `v2/graphql`. It asks `mods(filter, sort, offset, count)` for `totalCount` and each node's ID, name, summary, author, version, picture, last update, endorsements, downloads, adult content, status and game.
- **The filter.** Every condition must hold:
  - `gameDomainName` EQUALS the game;
  - `nameStemmed` MATCHES the words, so "texture" finds "Textures";
  - `author` MATCHES the author;
  - `adultContent` EQUALS false, unless adult content is asked for.
- **The sort** is one of `relevance`, `endorsements`, `downloads`, `updatedAt` or `createdAt`, `DESC` or, when asked, `ASC`.
- **The results.** A node becomes `ModInfo` with the same page link and availability rules as the catalog. A node of another game is left out. A negative count is dropped.
- **GraphQL errors.** GraphQL reports errors with HTTP 200, so the client checks for them in every answer and turns them into an `HttpRequestException` with their messages.
- **An answer the client can't read**, from v1 or v2, is now an `HttpRequestException` rather than a `JsonException`, so an API change is a failure to report, not a crash.
- **`GetModAsync`** also fills in the counts, from `mod_downloads` and `endorsement_count`.

### Recording it

- **`-SearchOnly`** in the recording script records the searches with either account. A full Premium run records them too.
- **The same query.** The script reads `SearchMods.graphql` itself, and builds the variables as `NexusSearchVariables` does. Each recording keeps the operation's name and variables, so a test can match its request to the recorded answer.
- **What it records.** Words, a second page, an author, the download and newest sorts with adult content, the oldest first, and words that match nothing.
- **The schema.** The script also records the parts of the schema the search relies on: the `mods` field's arguments, `ModsFilter`, `ModsSort`, `Mod` and the comparison operators. If the query is wrong, that shows how to fix it.
- **Personal data.** Member IDs written in camelCase, as GraphQL writes them, are now replaced like the snake_case ones.

### Application and the CLI

- **`IModLookup.SearchAsync`** passes a search to the game's source. **`IModLookup.SearchableSources`** lists the sources that can search.
- **`mod search [words] --game <game>`** lists a page of mods, with author, endorsements, downloads, last update and link. Adult and hidden mods are labelled.
- **Its options:** `--author`, `--adult`, `--sort`, `--ascending` to put the least first, `--page`, and `--page-size` from 1 to 100, 20 unless set. `--source` is needed only when more than one site can search.
- **`mod show`** also prints the counts.

## Consequences

- With a linked account, the CLI can find a mod by name and go on to `mod show` and `download` with its link.
- **The query is confirmed.** A Premium run on 2026-10-30 recorded the seven searches without an error: words, author, each sort, ascending, a second page, adult content and no results. Tests replay them, each answered only when the source sends the recorded variables, which a test compares in full, so the query, its filter names and `ASC` stand as written. Answers written by hand still cover what the recordings don't show, such as a mod with fields missing.
- **A second source** implements `IModSearch` with its own query, and maps the sorts it has. A sort it doesn't have is an `ArgumentException`.
