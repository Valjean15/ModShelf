# 80. Agent access

- Status: Accepted
- Date: 2026-11-06

## Context

Since 0.9.1 (ADR 0040), the command line's `mcp` runs ModShelf's MCP server on standard input and output, which an
agent's app, such as Claude Desktop or Claude Code, starts once the user configures it by hand, as the README says: the
command line's path, `mcp`, and the library's environment variables when it isn't the default. Any agent configured
so could use ModShelf; nothing in the app said so, or turned it off.

Before 1.0.0, the human asked (2026-11-06) for a setting the user opts in with, and for an easy way to connect an agent,
like NVIDIA App's Settings: **Allow agents to control NVIDIA App**; **Connect your agent** with **Copy prompt**, a
prompt to paste into the agent's chat ("Add a Streamable HTTP MCP server named … at http://127.0.0.1:13508/mcp to my
user-level MCP configuration on this PC. If you cannot configure MCP servers directly, show me where to add it
manually."); and **Advanced details**, the server's details.

The human chose, the same day:

- **Both ways in**: the command, as now, and an address, as NVIDIA's, while the desktop app runs; the second with the
  ASP.NET Core package of the MCP SDK, a new package this choice approved.
- **Off by default**, for every library, the human's own too; while off, the server **refuses each call, saying why**,
  rather than not starting.
- On Settings › Agents: **Copy prompt**, **View details**, and **Add to Claude Desktop**, which writes Claude Desktop's
  configuration after asking.

## Decision

### The setting

- `AppSettings.AgentsEnabled` (`bool?`, null for no) and `AgentsPort` (`int?`, null for `DefaultAgentsPort`, 47823;
  from 1024 to 65535), with `ISettingsService.SetAgentsEnabledAsync` and `SetAgentsPortAsync`: public additions the
  choice of a setting approved. `"agents": { "enabled": true, "port": 47823 }` in `settings.json`, each left out at its
  default; `settings set agents.enabled true` and `agents.port` on the command line.
- **Only the user changes them.** The MCP tools `settings_set` and `settings_reset` refuse both: "Whether AI agents may
  use ModShelf, and the port they reach it at, are the user's to change, in ModShelf's Settings, Agents, or with the
  CLI." An agent that can run commands could run the command line itself; the server doesn't hand it the key.

### The server, while agents are off

- A call filter, before the arguments' check (`AgentAccess`), reads the setting at each call: off, the call is the
  tool's error, "AI agents are off in ModShelf: the user hasn't allowed them to use it. Ask the user to turn on "Allow
  agents to use ModShelf" in ModShelf's Settings, Agents, then call again.", logged with the tool's name (event 1420).
  The server still starts and lists its tools, so the agent can say what to do, and turning agents on needs no restart
  of the agent's app. Settings that can't be read count as off, saying why (1421).
- Both ways in share the server's options (`ModShelfMcpServer.Configure`): its name, `modshelf`, its instructions, its
  tools and the checks of each call.

### The address

- `mcp --http` serves the server at `http://127.0.0.1:<port>/mcp`, Streamable HTTP, **stateless**, each request on its
  own, so any number of agents' apps can use it; `--port` (0 for any free one, which the output says), `agents.port`'s
  by default; `--parent <id>` ends it when that process ends. It's the MCP SDK's `ModelContextProtocol.AspNetCore`
  2.3.0, beside `ModelContextProtocol.Core` 2.3.0, on Kestrel, listening on 127.0.0.1 alone; the command line now has
  ASP.NET Core's shared framework, which also brings what `Microsoft.Extensions.Hosting` gave it, so that package
  reference went (NU1510).
- **This computer's apps alone.** Before the SDK, a request whose Host isn't `127.0.0.1`, `localhost` or `[::1]` at the
  port, or whose Origin, when there's one, isn't such a page, is refused with 403 (1424): a web page elsewhere that
  makes the browser name this computer, so as to reach its servers, says so in one of them. No token: as with the
  command, the user's own apps are trusted, and agents being off is the guard.
- **Port in use**: the command line says so and ends with a new exit code, 4 (`ExitCodes.PortInUse`).
- **The desktop app runs it** while agents are on (Platform's `AgentServerProcess`, implementing Application's
  `IAgentServer`): the command line beside the app, as `mcp --http --port <agents.port> --parent <its own id>`, with
  the library's `ModShelf__…` variables, without a window; served once it says its address (within 30 seconds), and
  stopped when agents are turned off, the port changes, or the app quits; and should the app end without stopping it,
  it ends with its parent. A process of its own, rather than in the desktop app: the MCP server and its tools are the
  command line's, and the desktop app references no web host. Where it stands is one of: off, starting, running, port
  in use, no command line, or failed with its exit code (events 1080–1083).
- **Where the command line is** (`CommandLinePath`): `ModShelf__CommandLine` when set; beside the app, as installed;
  or, in a build from source, in `src/ModShelf.Cli/bin/<configuration>/<framework>` beside the app's. In an AppImage it
  runs from where the system mounts it, somewhere new each time, so it serves the address but is no command for
  agents' apps to start.

### Settings › Agents

A new category, after Links (`AgentsViewModel`):

- **Allow agents to use ModShelf**, a toggle: saved at once, and the address served or stopped as it says.
- **Connect your agent**: **Copy prompt**, **View details** and **Add to Claude Desktop…**, with whether Claude Desktop
  has ModShelf, and the command, or why there's none.
  - The prompt follows NVIDIA's: "Add a local MCP server named "modshelf" to my user-level MCP configuration on this PC.
    It starts the command "…ModShelf.Cli.exe" with the argument "mcp" [and these environment variables: …]. If you can
    only add servers by address, add a Streamable HTTP MCP server named "modshelf" at http://127.0.0.1:47823/mcp
    instead, which works while the ModShelf app runs. If you cannot configure MCP servers directly, show me where to add
    it manually." Without a command, the address alone. The variables are the library's database and secrets folders
    and the secrets' scope, only when set, never the Nexus Mods client ID.
  - The details (a dialog): the name, the command, its arguments and variables, the address, Claude Desktop's JSON and
    Claude Code's `claude mcp add` commands, by command and by address, each with **Copy**.
  - **Add to Claude Desktop…** (Platform's `ClaudeDesktopConfigFile`, implementing `IClaudeDesktopConfig`): asks first,
    naming the file and the copy; adds the server named `modshelf` to `mcpServers`, as `{ "command", "args", "env" }`,
    in place of another by that name (asking to **Replace** it), keeping the file's other servers and settings; keeps
    the file as it was as `claude_desktop_config.modshelf-backup.json`; and replaces the file in one step. A file that
    isn't a JSON object, or whose `mcpServers` isn't one, is left as it is, saying so. The folder: `%APPDATA%\Claude` on
    Windows, or, for the Store's Claude Desktop, the copy the system keeps in its package,
    `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude`, which that app reads; `~/Library/Application
    Support/Claude` on macOS; `~/.config/Claude` on Linux. Not there, Claude Desktop isn't found. The Settings screen
    looks again each time it's shown.
- **Address**: where it stands, warm when it can't be served, and the **Port** with **Save**.

## Consequences

- After updating to 1.0.0, an agent configured before gets every call refused until the user turns agents on, which
  says how.
- The installed app is larger by ASP.NET Core's shared framework, which the command line's self-contained publish now
  carries beside the desktop app's.
- The address answers any app on this computer while agents are on, as the command does any app that starts it.
- Claude Desktop documents local servers as commands it starts, so **Add to Claude Desktop** writes the command; the
  address is for apps that reach servers by address, such as Claude Code with `--transport http`.

## Testing

- The setting: Application's range and default; `settings.json` read, written, refused out of range, and left out at
  its defaults; the command line's set, reset, refusals and list.
- The server: off, the tools listed and every call refused, saying how to turn agents on, and logged; turned on
  meanwhile, the next call goes on, and off again; settings that can't be read; the MCP tools refusing to change either
  setting, in any case.
- The address, as its own process: an agent's app reaching it with the SDK's Streamable HTTP client, its tools and
  instructions; off, refused; another Origin or Host refused with 403, and which requests count as this computer's; a
  port in use, exit code 4; ending with its parent; the port from the settings.
- The desktop app's side: the command line found as configured, beside the app or in its build folder; the address
  started for this process at the port, with the library's variables, kept at the same port, moved, stopped, port in
  use, another exit, never ready, ending while served, no command line, an AppImage's, disposed; Claude Desktop's
  folder on Windows, the Store's too; its file: not found, a new file, others kept with a copy, replaced, unreadable left
  as it is, empty. The host's options: the variables from the configuration, never the client ID.
- Settings › Agents: off by default; as stored; turned on and off, saved and served; a change that can't be saved;
  unreadable settings; each state said; the port saved, its default left out, out of range refused; the prompt, with the
  variables and the port, without a command, while off, the clipboard refusing; the details, copied, without a command;
  Claude Desktop: each state, added and replaced after asking, declined, changed meanwhile, unreadable, unwritable,
  without a command, looked at again when the screen's shown again; the snippets as Claude Desktop and Claude Code read
  them.
- 38 deliberate bugs, all caught. One got through at first, the address's status naming the default port rather than
  the one in use, as the test used the default: it now uses another.
- Looked at here, on Linux: Settings › Agents off and on, the address answering, the details, adding to a stand-in
  Claude Desktop's file with another server kept, a port in use said in the warm colour, the address ending with the
  app; the desktop app and the command line published together, as the installers do, the app running the address.
- By hand: `docs/manual-checklist-mcp.md` new section 0.
