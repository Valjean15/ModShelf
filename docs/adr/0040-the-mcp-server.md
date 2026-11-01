# 40. The MCP server

- Status: Accepted
- Date: 2026-11-01

## Context

The human uses an AI agent as the bridge between ModShelf and a mod manager, Fluffy Mod Manager: ModShelf downloads
and keeps the versions, the mod manager installs, and the agent compares what came into the library with what's
installed, and replaces it. The agent needs to see the library, its updates, the queue and the settings, and to act:
check for updates, download a file into the library, change a download or a setting. The Model Context Protocol (MCP)
is how agents' apps, such as Claude Desktop and Claude Code, reach a program's tools.

The human approved: the server in the CLI, shipped with the app, so it updates with it; the tools as listed below,
leaving out linking accounts, deleting files, moving the library and taking or giving back the sites' links; a tool for
what's new in the library; the official C# SDK; and, for a queue the desktop app runs, asking the desktop app.

One process runs a library's download queue (ADR 0027): the desktop app, while it runs. Another process, such as the
CLI, can list the queue as saved, but can't add to it or change it.

## Decision

- **`modshelf mcp`** (`ModShelf.Cli mcp` in an installed ModShelf) runs the MCP server on standard input and output,
  until its client closes them. It builds the CLI's host, without downloads, and serves 19 tools, each returning
  structured content with an output schema, and annotated as reading only, changing, destructive or reaching the
  sites. A problem the agent or the user can act on, such as a link no site knows, is the tool's error in words;
  anything else is a generic error, with the details in the log. Logs go to standard error and the log files, never to
  standard output, which is the protocol's.
  - Reading: `library_list` (the mods, their files with absolute paths, versions and whether they're archived, and their
    updates), `library_changes` (the files downloaded since a time, each with its mod's other files, and what
    `library.oldVersions` does), `library_updates` (with the file each replaces, and with changelog entries if asked),
    `downloads_list`, `mod_show`, `mod_search`, `settings_show`, `accounts_status` (linked or not, and the level; never
    whose account or its key) and `diagnostics`.
  - Acting: `library_check`, `library_watch`, `library_unwatch`, `download`, `downloads_pause`, `downloads_resume`,
    `downloads_retry`, `downloads_cancel` (which deletes only the part of the file downloaded), `settings_set` and
    `settings_reset`. A download's ID can be its first 8 characters or more, as `queue list` shows them.
- **The library records no replacement history**: a library file knows its mod, version and dates, but not which file
  it replaced. `library_changes` gives each new file with its mod's other files in the library instead, so the agent
  sees the versions it may replace, and whether they're archived; with `library.oldVersions` at a number, the older ones
  may be gone. An update knows the file it replaces, from the site.
- **Through the desktop app.** Application gains `IQueueRequests`, which carries out a request to download a link or
  to pause, resume, retry or cancel a download in the process that runs the queue, with every problem as a reply; and
  the port `IQueueOwner`, the desktop app reached from another process. Platform implements it with a second pipe of
  the single instance's (ADR 0029), named after the library with `-queue`, same user only, a line of JSON there and
  back, enums by name so an unknown one is refused, not misread. The desktop app answers on it while it runs; an app
  from before 0.9.1 has no such pipe, and is taken as none.
- **Without the desktop app, the server runs the queue**, in a host of its own with downloads, until no download is
  under way: queued, resolving, downloading or verifying. A download waiting for the user to start it on the site isn't,
  so the desktop app, which the site's `nxm://` link starts, can take the queue over. Closing the server stops its
  queue: what's left goes on the next time a ModShelf runs the downloads. While another process that takes no requests
  runs the queue, such as a `download` command, a request is refused and says so.
- **A download's reply waits up to 10 seconds for it to start**, so it says how it went: downloading, or waiting for
  the user, with the page to start it on. The desktop app shows the user that page too. The pages live in the memory of
  the process that runs the queue, so `downloads_list` has them while the server runs the queue, not from the database.
- **The site's problems are said for the user** in a request's reply: a link to no mod or file, an account to link, a
  site asking to slow down. The server never links accounts: it says where the user does.
- **Links are never logged**: requests are logged by kind only. The SDK's own logging goes through a guard that lets
  its warnings and errors through, but none whose values carry a message's content, and not a tool's answer.
- **The settings' names and changes** move from `settings` to a class both use, `SettingChanges`, so that the server
  changes them as `settings set` does, in the same words.
- **The CLI ships with the app**: the pack scripts publish it, self-contained, into the desktop app's folder, where it
  shares the runtime and libraries (3 MB more). Its settings file is `appsettings.cli.json`, read right after
  `appsettings.json`, which is the desktop app's in that folder; an architecture test keeps the two apps' own files'
  names apart.
- **ModelContextProtocol.Core 2.3.0**, the official SDK's core, without its hosting package: the server runs inside the
  CLI's command, on streams the CLI's environment opens, which lets the tests run it in process.

## Consequences

- An agent's app starts the installed CLI: `%LOCALAPPDATA%\ModShelf.App\current\ModShelf.Cli.exe mcp` on Windows, and
  `ModShelf.app/Contents/MacOS/ModShelf.Cli mcp` on macOS. The Linux AppImage holds the CLI but starts only the
  desktop app, so on Linux the server comes from a build, or from the extracted AppImage. The README shows the
  configuration for Claude Desktop and Claude Code.
- The MCP server updates with the app; one already running keeps its version until its client starts it again, and
  says so when a newer desktop app's reply can't be read. On Windows, applying an update ends the processes running
  from the app's folder (Velopack's updater does), the MCP server among them: its downloads go on later, and the
  agent's app has to start it again, as Claude Desktop does when it restarts.
- A queue request to the desktop app that times out may still have been carried out; the error says so, and
  `downloads_list` shows the queue.
- The server is a second way into the library for whatever the agent is told to do; it can't link accounts, delete the
  library's files, move the library or take the sites' links, and it never installs mods.

## Testing

- Application: queue requests, each kind and each problem as a reply, the wait for a download to start on a clock
  that moves as it's read, and the link never logged.
- Platform: the messages there and back, and refused when they aren't ones; the pipe between an asking side and a
  listening one: replies, no app, another library's, requests side by side, timeouts, hanging up, unreadable replies
  and requests, stopping while one is carried out, the socket in the user's runtime folder, and no link in the logs.
- CLI: the server in process, with a client on pipes: the tools and their annotations, the settings, downloads it runs
  itself and lets go of, a download waiting for the user with its page, changes by ID or its start, a desktop app
  stood in (its reply, its refusal, its silence), what came into the library with the versions before, updates with what
  they replace, the library, a mod and a search, the accounts without names or keys, and no key in the logs at Trace.
  The queue's own tests run it with a host of fakes. And `modshelf mcp` as a process an SDK client starts over stdio.
- Architecture: both pack scripts publish the CLI into the desktop app's folder, and the apps' own files have names
  of their own.
- By hand: packed for Linux, the AppImage holds both apps and their settings files; the CLI in the publish folder
  serves `downloads_list`. `docs/manual-checklist-mcp.md` has the checks with an agent's app.
