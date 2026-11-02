# 41. The MCP server's first fixes

- Status: Accepted
- Date: 2026-11-01

## Context

The human used the MCP server of 0.9.1 (ADR 0040) through Claude Desktop on a library of 73 mods, and reported what
failed and what got in the way:

- **`mod_show` failed** with "data must have required property 'changelog'" without its changelog, and with
  "data/files/1 must have required property 'libraryPath'" for any mod with a file the library doesn't have.
- **Deleted files** were listed as if they were still there.
- **`library_list` answered about 53,000 characters**, more than an agent reads at once.
- **A pulled file was invisible.** Dynamic Menu Overhaul's 2.7 says it was replaced by 2.8, which the site no longer
  lists but the library has, and nothing said so.

The decisions for the rest of the feedback are in the 0.9.2 plan: forgetting a mod, new optional files, SHA-256 and
rescanning, each a step of its own.

## Decision

- **Results include their nulls.** The SDK writes each tool's output schema from its result record, with every
  property required and null allowed where it's optional. Its default JSON options left nulls out of the results,
  so a client that checks results against the schema, as Claude Desktop does, refused any result with an optional
  field unset. The tools now write results with the SDK's options, nulls included.
- **Missing files.** Each library file in a result says whether it's `missing` from disk, and each mod how many of its
  files are (`missingCount`). Only the agent's view changes: the library still has the file, to download again.
- **`library_list` a page at a time.**
  - It lists 50 mods unless `limit` (up to 200) says otherwise, from `offset`, with the `total` and whether there's
    more.
  - It takes filters: a game, a site or a mod ID, and watched mods or mods with updates only.
  - `compact` gives each mod's counts (`fileCount`, `missingCount`, `updateCount`) in place of its files and updates.
- **Pulled files.**
  - In `mod_show`, a file's `replacedByOnSite` says whether the site still lists the file named as its replacement.
  - `libraryOnly` lists the library's files of the mod that the site no longer lists.
  - `newerInLibrary` says when one of those is newer than everything the site lists, as with 2.8.

## Consequences

- An agent reads a large library a game, a mod or a page at a time, and sees what's on disk and what isn't.
- The new properties appear in the schemas, so a client sees them as it connects.
- A missing file is listed until the library drops it: forgetting a mod (0.9.2 step 2) and rescanning (step 5) do.

## Testing

- **The CLI's in-process tests check every tool result against its tool's output schema**, as clients that check
  results do: each required property present, each value of a type the schema allows. Without the fix, the existing
  `mod_show` tests fail that check.
- **The reported cases as tests:**
  - a mod with a file the library doesn't have, and a lookup without its changelog;
  - a file deleted from disk, in `library_list`, `mod_show` and `library_changes`;
  - a library read two mods a page, compact, by site and game, by mod, and watched-only and updates-only;
  - a page it can't give;
  - a file the site pulled that the library has.
- **16 deliberate bugs**, all caught by the tests, three of them after the tests were made stricter. They were a
  missing count that counted the files on disk, a last page that said there was more, and a game filter that let
  every game through.
