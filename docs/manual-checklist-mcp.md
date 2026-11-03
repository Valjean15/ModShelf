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
- [ ] **2.3a Several links.** Ask the agent to download three mods at once, one of them a link that isn't a mod's (such
  as `https://www.nexusmods.com/stardewvalley`).
  One call to `download` with three links: two downloading or waiting for you, and the third's result says why it
  couldn't be downloaded. The summary says "Of 3 links, …".
- [ ] **2.4 What came in.** Ask the agent what came into the library since this morning, and which versions they
  replaced.
  The new files, each with the version it replaced and what became of it (`replaces`: archived when
  `library.oldVersions` is `archive`, deleted when the newest versions kept leave it out, kept otherwise), and the mod's
  other files. A file whose mod had none before replaces nothing (`replaces` is null).
- [ ] **2.5 New optional files.** For a watched mod you have files of whose author has added an optional file since its
  last check, such as Bestiary, ask the agent to check for updates.
  The optional file is listed with kind `newFile`, replacing nothing; the desktop app's Updates screen says "New
  optional file". Once it's downloaded, it leaves the list.
- [ ] **2.6 Hashes.** Ask the agent for the SHA-256 of a file downloaded in 2.1, and compare it with
  `Get-FileHash -Algorithm SHA256 <its path>` in PowerShell.
  They match (`Get-FileHash` writes it in capitals). A file from before 0.9.2 has none (`sha256` is null).

- [ ] **2.7 Pins, skips and notes.** For a watched mod with an update, ask the agent to pin it, then for the updates;
  to unpin it and skip that update, then for the updates again; to offer it again; and to keep a note of the mod.
  The pinned mod's update, then the skipped one, is left out of `library_updates`; `library_list` says `pinned`, the
  note, and the update's `skipped`, and `mod_show` too. A desktop app running meanwhile shows the changes once it starts
  again.

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
- [ ] **3.5 Rescanning.** Download a file of a watched mod with the site's **Manual download** into the mod's folder in
  the test library, as Female Nude Blend's was, then ask the agent to rescan the library.
  The file is added, with its SHA-256; the files from before 0.9.2 get theirs; a file of your own in the folder is listed
  as matching nothing, and stays. Rescanning the mod forgotten in 3.4 by its link brings it back, watched.

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
| 2.3a Several links | | |
| 2.4 What came in | | |
| 2.5 New optional files | | |
| 2.6 Hashes | | |
| 2.7 Pins, skips and notes | | |
| 3.1 A setting | | |
| 3.2 What it can't do | | |
| 3.3 The logs | | |
| 3.4 Forgetting a mod | | |
| 3.5 Rescanning | | |
| 4.1 With the app | | |
