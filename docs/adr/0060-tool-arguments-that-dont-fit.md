# 60. Tool arguments that don't fit

- Status: Accepted
- Date: 2026-11-03

## Context

The human's agent called `download` four times with `{"link": "…"}`, as the tool took before 0.9.3; it takes `links`, a
list, since step 10b. The MCP SDK binds a call's arguments before the tool runs: it threw "The arguments dictionary is
missing a value for the required parameter 'links'", logged it, and gave the agent only "An error occurred invoking
'download'.", so the agent couldn't tell what was wrong, and looked for the cause on disk. The CLI and the desktop app
downloaded the same file. An agent's app reads the tools when it starts, and a conversation may go on with what it read
before ModShelf was updated. An argument of another type, such as `limit: "many"`, failed the same way.

## Decision

- A call-tool filter (`ToolArguments.Explained`) checks a call's arguments against the tool's input schema before the
  tool runs: each required argument given, and each argument given of a type the schema allows (a list's items too,
  `null` where allowed). When one isn't, the call fails as the tool's error, without running it, saying what's wrong,
  what the tool takes (the required ones marked), the arguments given that it doesn't take, and that the agent's list of
  the tools may be older than the server: restarting the agent's app and starting a new conversation reads it again.
- An argument the tool doesn't take, given beside the ones it needs, is still left aside, as the SDK does.
- No tool takes its old arguments as well: the agent corrects its call from the error.

## Consequences

- An agent with an older list of the tools learns the new arguments from the first failed call.
- A value of the right type but out of range, such as a number too large for an int, still fails as the SDK says.

## Testing

- MCP: `download` with `link`; `links` as a string and as a list with a number; `library_list`'s `limit` as a string and
  as 2.5, and `compact` as a string; `library_changes`' `since` as null; each failing with what's wrong and what the tool
  takes, downloading nothing; and a call with a nullable argument as null and an argument the tool doesn't take still
  reaching the tool.
- 11 deliberate bugs, all caught.
- By hand: `docs/manual-checklist-mcp.md` 1.1.
