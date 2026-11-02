# Manual checklist: the MCP server

The tests run the MCP server in process and as a process an MCP client starts, with fake sites; they never run an
agent's app, Nexus Mods, or the desktop app beside the server. Before a release, check those by hand with an installed
ModShelf, on Windows at least, with Claude Desktop or Claude Code configured as the README says ([ADR 0040](adr/0040-the-mcp-server.md)).

Use a test library, as the installer checklist's settings do (`ModShelf__DatabasePath`, `ModShelf__SecretsFolder` and
`ModShelf__SecretScope`), set in the server's `"env"` as well as for the desktop app, and a Nexus Mods account linked
there. Never paste an API key or an `nxm://` link into a report: both carry keys. Each check says what to do, then what
you should see.

## 1. The server

- [ ] **1.1 Tools.** Start the agent's app, and ask it which ModShelf tools it has.
  20 tools, from `library_list` to `diagnostics`. Claude Desktop's settings show the server as running.
- [ ] **1.2 Reading.** Ask for the library, the updates and the settings.
  The mods, their files' full paths in the test library, and the settings with their defaults marked.
- [ ] **1.3 Accounts.** Ask which accounts are linked.
  Nexus Mods linked, at its level, such as Premium; never the account's name or key.

## 2. Downloads

- [ ] **2.1 Without the desktop app.** Quit the desktop app. Ask the agent to download an update from `library_updates`.
  Premium: it says this MCP server downloads it, and the file comes into the library; `queue list` in a terminal shows it
  done. A few seconds after, `download` in a terminal runs: the server let the queue go. Free: the reply gives the page
  to start it on the site; start it there, with the mod manager download: the desktop app starts, and downloads it.
- [ ] **2.2 With the desktop app.** Start the desktop app, and ask the agent to download another update.
  It says the desktop app downloads it, and the Downloads screen shows it. With a free account, the screen shows the
  download waiting, with **Open on Nexus Mods**.
- [ ] **2.3 Changing a download.** Ask the agent to pause it, resume it, then cancel another.
  The Downloads screen follows each change; cancelling deletes only the part downloaded.
- [ ] **2.4 What came in.** Ask the agent what came into the library since this morning.
  The new files, each with the mod's other files: the old version, archived when `library.oldVersions` is `archive`.

## 3. Settings and refusals

- [ ] **3.1 A setting.** Ask the agent to set `library.oldVersions` to `archive`, then to reset it.
  The desktop app's Settings screen shows each, once it's opened again.
- [ ] **3.2 What it can't do.** Ask the agent to link an account, delete a file, or move the library.
  It has no tool for these, and says so; nothing changes.
- [ ] **3.3 The logs.** Open the test library's `logs\cli-….log`.
  The server's start and end, and what it did with the queue; no link, key or `nxm://`.
- [ ] **3.4 Forgetting a mod.** Ask the agent to forget a mod with a downloaded file.
  It says the mod is out of the library and where its file stays; the file is still there, and `library_list` no longer
  lists the mod. A desktop app running meanwhile shows it until it starts again. While a download of one of its files
  into the library isn't finished, the agent says it can't.

## 4. Updating

- [ ] **4.1 With the app.** Update the desktop app to a newer release, then restart the agent's app.
  `%LOCALAPPDATA%\ModShelf.App\current\ModShelf.Cli.exe --version` is the new version, and the server starts as in 1.1.

## Results

Copy this table into the release's notes or issue, and mark each check ✅, ❌ or – (not run). For a ❌, add what you
asked, what the agent and the app showed, and the matching lines of the log.

| Check | Result | Notes |
|---|---|---|
| 1.1 Tools | | |
| 1.2 Reading | | |
| 1.3 Accounts | | |
| 2.1 Without the desktop app | | |
| 2.2 With the desktop app | | |
| 2.3 Changing a download | | |
| 2.4 What came in | | |
| 3.1 A setting | | |
| 3.2 What it can't do | | |
| 3.3 The logs | | |
| 3.4 Forgetting a mod | | |
| 4.1 With the app | | |
