# 68. CLI and MCP catch up

- Status: Accepted
- Date: 2026-11-04

## Context

Steps 1 to 6 of 1.0.0 added settings and search abilities, and most reached the CLI and the MCP server with their
steps. Four things were left:

- **Deprecated arguments** existed only for `download`'s `link` (ADR 0061). Its code, block, note and warning were
  written for that one argument, and nothing said whether agents still used it, so no one could know when it could go.
- **The search** in `mod search` and `mod_search` couldn't narrow by category or by when mods were updated, though Browse
  can. An agent learned what a site's search could do only from a refusal.
- **The CLI's tracking** said "Tracking mod 12604 of skyrimspecialedition on nexus." for every site: Nexus Mods' word,
  and the site's ID.
- **Messages** named a site by its ID, such as "mod 1 of game on nexus".

The human chose (2026-11-04):

- the count kept in `deprecated-calls.json` beside `settings.json`, a state file named for what it holds (ADR 0066);
- the rule as one list of deprecations and one helper, with `"deprecated": true` in the tool's input schema;
- `category` and `updatedWithin`, and a `search` block in each `mod_search` result;
- the site's words and name in prose, the IDs kept where they're typed, and the commands', fields' and sorts' names
  kept.

## Decision

### Deprecated arguments, as a rule

An argument renamed in a release still works until the next major version.

- **The list:** `Cli/Mcp/Deprecations` holds each one as a `Deprecation`:
  - its code, such as `DL_LINK_ARG`;
  - the tool and the old argument, and the argument to use;
  - the version that removes it, `2.0`;
  - the note its message starts with.

  Its name in the count is the tool and the argument, such as `download.link`.
- **The argument** is marked `[Deprecated("DL_LINK_ARG")]`, and its description starts "Deprecated, and stops working
  in ModShelf 2.0:". Its input schema says `"deprecated": true`, as JSON Schema has it. The server sets it once the SDK
  has made the tool, since the SDK's schema hook doesn't see a parameter's attributes.
- **A call that uses it** goes through `McpDeprecations.NoteAsync`, which:
  - warns in the log, event 1415 for every deprecation: "DL_LINK_ARG download called with deprecated 'link'; use
    'links' (client: claude-ai)", with the agent's app's name but none of the arguments, since an `nxm://` link carries
    a key;
  - counts the call (below);
  - gives the result's `deprecated` block, `DeprecatedArgument<TExample>`: the code, the old argument, the one to use,
    `removedIn`, and the example, the call's arguments as they should be, with its own values.

  `McpDeprecations.Message` starts the result's message with "Note: " and the note.
- **`download`'s `link`** works as before. Its note is now "'link' is deprecated and stops working in ModShelf 2.0;
  pass 'links' as a list instead, as in the example.", and its block has `removedIn`.
- **A test** checks every listed deprecation:
  - the old argument is marked deprecated, its description says so and names the new one;
  - the new argument isn't marked, and the tool's result has a `deprecated` block;
  - every argument marked `[Deprecated]` is listed, with its code, and no other argument is marked.
- **The CLI** has no deprecated option. When one comes, the same rule applies: the old name stays as an alias, a note
  goes to standard error, and the same warning and count follow.

### The count

- **Application, `Diagnostics/`:**
  - `DeprecatedCall`: the name, the code, how many calls, the first and the last, and the last call's client.
  - `IDeprecatedCalls`:
    - `CountAsync(name, code, client)` counts a call made now. It never fails the call: a count that can't be kept is
      logged (event 1700). The client's name is kept to its first line, without control characters, and to 64
      characters.
    - `ListAsync` gives the calls counted, by name.
  - The port `IDeprecatedCallStore`.
  - `AddDiagnostics` registers it.
- **Infrastructure:** `JsonDeprecatedCallStore` keeps the count in `deprecated-calls.json` beside `settings.json`.
  `AddLibraryFiles` registers it through `AddDeprecatedCallsFile`, so it moves with the database.

  ```json
  {
    "download.link": {
      "code": "DL_LINK_ARG",
      "count": 3,
      "first": "2026-11-01T09:12:00Z",
      "last": "2026-11-03T22:31:00Z",
      "client": "claude-ai"
    }
  }
  ```

  - Several MCP servers can run at once, such as Claude Desktop's and Claude Code's, so counting opens the file alone,
    and reading lets other readers in. Each waits up to about 2 seconds for another to finish, then gives up, and the
    call goes on uncounted.
  - The file is written in place while it's held. A file that can't be read is forgotten, which is logged (event
    1701), and counting starts again.
  - A count that isn't what it should be is left out and starts again. What else the file holds stays.
  - Resetting the settings leaves it. Deleting it starts the count again.
- **The diagnostics report** ends with "Legacy arguments: download.link ×3 (last 2026-11-03 22:31 UTC, claude-ai)",
  by name, or "Legacy arguments: none". What the file says is kept to a line. The desktop app's **Copy diagnostics**,
  the CLI's `diagnostics` and the MCP server's all have it.
- **The report's folders** also count the games with folders of their own, from step 6, such as "library ~/Mods,
  2 games in folders of their own", without naming them.

### Search

- **The filters:**
  - `mod_search` takes `category` and `updatedWithin`;
  - `mod search` takes `--category <name>` and `--updated-within <days>`.
- **A category** is one of the game's, read from the site's catalog. It's matched in any case and trimmed, and sent as
  the game names it.
  - One the game doesn't have is refused, naming those it has: "Stardew Valley has no category 'Armour' on Nexus Mods.
    Its categories: …".
  - A game without categories: "… has no categories on …".
  - A site that can't search by category refuses it through Application, as before, without the game being read.
- **The days** run from 1 to 36,500, counted back from the start of the hour in UTC, so a search's pages asked within
  the hour find the same mods.
- **`mod_search`'s result** gains `search`, a `SiteSearch` of what the site's search can do for the game:
  - the site's name;
  - its orders, the site's own first, each with whether it goes either way round;
  - the filters, as the arguments that take them: `words`, `author`, `category`, `updatedWithin` and `adult`;
  - the largest page, and the site's word for approval;
  - the game's categories, or null when the site doesn't search by category or they can't be read.

  The server reads a game's categories once while it runs. The CLI reads the game only for `--category`.
- **Names kept:** `endorsements` and `--sort Endorsements` keep their names, the question ADR 0064 left. Each is
  described as whatever the site counts as approval, and `search.approvals` gives the site's word.

### The site's words and name

- **The CLI's tracking**, as a mod's page words it:
  - `mod track`: "Tracked on Nexus Mods: mod 12604 of skyrimspecialedition.";
  - `mod untrack`: "No longer tracked on Nexus Mods: …";
  - `mod tracked`: "12 tracked mods on Nexus Mods.", "1 tracked mod …" or "No tracked mods …", and "To untrack one:
    mod untrack "<its link>"".

  Another site says it in its own words, such as "Followed on Example Mods: …". The commands stay `track`, `untrack`
  and `tracked`, and their descriptions say "follow, such as Track on Nexus Mods". Where the site isn't known yet, the
  wording is ModShelf's: "can follow mods", "The site needs a linked account to follow the mod".
- **Prose names the site** by its name, through `CliText.SiteName` and `CliText.ModOn`:
  - watching and unwatching, in the CLI and the MCP server: "Watching SkyUI (mod 12604 of skyrimspecialedition on Nexus
    Mods).";
  - the rescan's and the update check's failures;
  - Application's "Mod 12604 of skyrimspecialedition on Nexus Mods isn't in the library.", by the site's ID when it's no
    longer here;
  - Application's refusal of a link to a site without a watchlist, now "can follow mods".
- **IDs stay where they're typed:**
  - the account commands, which take the site's ID;
  - `--source` and `source`, and the refusals that list the sites to choose from;
  - `links give-back`.

## Consequences

- A new deprecation is a list entry, the attribute, a description that starts "Deprecated", a `deprecated` block in
  the tool's result, and the two calls. The test checks the rest.
- `download`'s block gained `removedIn`, and its note and log warning read differently. A script that matched the old
  log text needs the new one.
- The count is a count, not a log: it keeps each argument's first and last calls and the last client, and nothing else.
- The first `mod_search` for a game while the server runs reads the game once more from its site.
- An agent's app sees the new arguments after it's restarted.
- The desktop app shows Application's messages in their new words.

## Testing

- Application:
  - counting: trimmed, at the time it's made, the client kept to a line and short, failures logged and the call going
    on, other failures not hidden, and blank names refused;
  - the report: the deprecated arguments by name, in UTC, with and without a client, kept to a line, none, and
    unreadable; the games in folders of their own counted;
  - a mod not in the library, named with its site's name, or its ID.
- Infrastructure, `deprecated-calls.json`:
  - the documented JSON, nothing for no file, a call without a client;
  - other names and keys kept, times in any offset;
  - broken files, empty files and counts that aren't what they should be;
  - the largest count, many counting at once without losing one, waiting while another has the file, giving up, and a
    folder in its place;
  - registration, and beside the database.
- CLI and MCP:
  - `link` with its block, note and warning; the calls counted in the file and the diagnostics;
  - every deprecation's schemas;
  - the filters, from the start of the hour, read once, and refused;
  - the `search` block for a site with everything and one with little, and categories that can't be read;
  - tracking in the site's words, one mod, and the refusals;
  - the sites' names in the library's, the rescan's and the update check's messages.
- 76 deliberate bugs, all caught. Four got through at first, and tests were added or made stricter:
  - a client's name cut at `\n` only, not `\r`;
  - times written to the file in their own offset, not UTC;
  - the MCP server reading the game for a category the site can't search by;
  - the site's orders listed in ModShelf's order, not the site's.

  One made the store retry forever, which hung the tests: the test now fails after 30 seconds instead. Four didn't build,
  for the analyzers and nullable checks, and were rewritten.
- By hand:
  - `docs/manual-checklist.md` 3.6, 8.1 to 8.4 and 10.3;
  - `docs/manual-checklist-desktop.md` 10.9;
  - `docs/manual-checklist-mcp.md` 1.5 and 2.3b.
