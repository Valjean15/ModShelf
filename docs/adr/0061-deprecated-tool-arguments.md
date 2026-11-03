# 61. Deprecated tool arguments

- Status: Accepted
- Date: 2026-11-03

## Context

`download` took one `link` until step 10b of 0.9.3, then `links`, a list of up to 50. An agent whose list of the tools
was older called it with `link`, which failed: before 0.9.4 with only "An error occurred", and since 0.9.4 (ADR 0060)
saying what the tool takes. Either way the agent's call failed, though what it asked for was clear. The human chose
(2026-11-03), from feedback written with their agent:

- a call with `link` and no `links` works, as `links: [link]`; one with both is refused;
- its result has a `deprecated` block for scripts, and its message starts with a note, because a model reads that text;
- a warning with a code goes to the log, so a search finds every old-style call;
- the tool's description says `link` still works but is deprecated.

Old arguments are marked deprecated rather than removed, as the 1.0.0 plan's step 7 makes the rule.

## Decision

- **`download`** takes `links` and `link`, both optional; the `McpServer` parameter, for the client's name, isn't
  in the schema.
  - `link` alone becomes `links: [link]`.
  - Both: "Pass only 'links'; 'link' is the old name for a single link."
  - Neither, or an empty list: "'links' needs at least one link."
  - More than 50: "Give from 1 to 50 links.", as before.
- **The result** (`DownloadsResult`) gains `deprecated` (`DeprecatedArgument`): `code` `DL_LINK_ARG`, `argument` `link`,
  `use` `links`, and `example`, the arguments as they should be given, `{"links": [<the caller's link>]}`. It's null for
  a call with `links`.
- **The message** starts "Note: 'link' is the old way to call download and will stop working; pass 'links' as a list
  instead, as in the example." before the usual summary.
- **The log** has a warning, event 1415: "DL_LINK_ARG download called with legacy 'link'; normalized to links=[1 link]
  (client: <the client's name>)". The link itself isn't logged: an `nxm://` link carries the site's key, which is never
  logged.
- **The descriptions:** `links` ends "(link, a single link, still works but is deprecated.)"; `link` says it's
  deprecated and to pass `links`.

## Consequences

- An agent with an older list of the tools downloads as it meant to, and learns the new argument from the result.
- `link` stops working at the next major version, 2.0.0.
- Counting deprecated calls in the diagnostics report, also suggested, needs a record kept between runs; it's left to
  the 1.0.0 plan's step 7.

## Testing

- MCP: `link` downloading, with the block, the note and the warning (without the link); `links` with no block; both
  refused; none, an empty list and 51 refused; `download`'s schema taking `links` and `link`, neither required, and
  `link` of another type refused by the argument check (ADR 0060), whose own test moved to `library_changes`.
- 10 deliberate bugs, all caught; one didn't build, for the analyzers, and was rewritten.
- By hand: `docs/manual-checklist-mcp.md` 2.3b.
