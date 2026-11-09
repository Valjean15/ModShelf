# 85. Several command line processes

- Status: Accepted
- Date: 2026-11-09

## Context

The human saw (2026-11-09) the desktop app and three `ModShelf.Cli` processes in Task Manager, and asked whether
they came from several Claude conversations. Their logs showed two `mcp` servers started a second apart, at
07:22:49 and 07:22:50, and the `mcp --http` the app runs while agents are on (ADR 0080). Claude Desktop starts its
local MCP servers once for the app, not for each conversation, but on Windows it starts each of them twice, a known bug
of its own; Claude Code starts one for each session. Each stays while its app runs.

Each opens the library database when its agent calls it, and before 1.0.1 a change by one could make the app's screens
wait (ADR 0081–0083). The plan offered a relay: an `mcp` started by an agent's app passing its calls to the app's
address when the app serves one, so one process alone used the database. The human chose (2026-11-09) to explain the
processes rather than relay: writing ahead and the database off the UI thread make them harmless.

## Decision

- **No relay.** Each `mcp` keeps working with the library itself, as since 0.9.1.
- **Settings › Agents** says, under **Address**, that each agent's app starts the command line for itself while it
  runs, Claude Desktop on Windows twice, and that the address is one more: several `ModShelf.Cli` processes are
  expected, and wait for their agents' calls (`AgentsProcessesNote`).
- **The README** says the same, that each one's log starts with what it runs and the process that started it (ADR
  0081), and how to see them in Task Manager's **Details** or with PowerShell.

## Consequences

- An agent's server still reads and writes the library on its own: two changes at once take turns, as two writes always
  did; a read never waits (ADR 0082).
- Should the processes ever matter again, the logs name each one and its parent, so the relay can be weighed on facts.

## Testing

- Looked at here: the note on Settings › Agents, under the port.
- The start lines that tell the processes apart are tested with ADR 0081.
