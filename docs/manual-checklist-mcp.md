# Manual checklist: the MCP server

The tests run the MCP server in process and as a process an MCP client starts, with fake sites; they never run an
agent's app, Nexus Mods, or the desktop app beside the server. Before a release, check those by hand with an installed
ModShelf, on Windows at least, with Claude Desktop or Claude Code configured as the README says ([ADR 0040](adr/0040-the-mcp-server.md)).

Use a test library, as the installer checklist's settings do (`ModShelf__DatabasePath`, `ModShelf__SecretsFolder` and
`ModShelf__SecretScope`), set in the server's `"env"` as well as for the desktop app, and a Nexus Mods account linked
there. Never paste an API key or an `nxm://` link into a report: both carry keys. Each check says what to do, then what
you should see.

## 0. Agents allowed, and connecting them

Settings › **Agents** in the desktop app. Agents are off until you turn them on; the other sections need them on.

- [ ] **0.1 Off.** With **Allow agents to use ModShelf** off, ask the agent for the library.
  It says AI agents are off in ModShelf, and to turn on "Allow agents to use ModShelf" in Settings, Agents; it can
  still list ModShelf's tools. The Agents card says "Not served: agents are off."
- [ ] **0.2 On, without a restart.** Turn it on, and ask again in the same conversation.
  The library, without restarting the agent's app. The card says "Served at http://127.0.0.1:47823/mcp while
  ModShelf runs."; `settings.json` holds `"agents": { "enabled": true }`.
- [ ] **0.3 Copy prompt.** In Claude Code, **Copy prompt** and paste it into its chat.
  Claude Code adds the server named `modshelf` for your user, with the command line's path, `mcp`, and the test
  library's `ModShelf__…` variables; `claude mcp list` shows it connected, and a new session lists its tools.
- [ ] **0.4 View details.** **View details**, then **Copy** beside each.
  The name, the command, `mcp`, the variables, the address, Claude Desktop's JSON and Claude Code's two commands; each
  **Copy** puts its value on the clipboard and says "Copied".
- [ ] **0.5 Add to Claude Desktop.** Quit Claude Desktop; note its other servers. **Add to Claude Desktop…**, then **Add**.
  The question names its `claude_desktop_config.json` (on Windows, `%APPDATA%\Claude`, or the Store app's copy under
  `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Roaming\Claude`) and the copy kept beside it,
  `claude_desktop_config.modshelf-backup.json`. Then "Added to Claude Desktop"; the card says "Claude Desktop has
  ModShelf.", and the button is off. The file has `modshelf` and the other servers as they were. Start Claude Desktop:
  its tools are ModShelf's 25.
- [ ] **0.6 The address.** With the desktop app running and agents on, in a terminal:
  `claude mcp add --transport http --scope user modshelf-address http://127.0.0.1:47823/mcp`, then a new Claude Code
  session.
  Its tools are ModShelf's. Quit the desktop app: the address ends (the session's calls fail), and Task Manager shows
  no `ModShelf.Cli.exe` left. `claude mcp remove modshelf-address` afterwards.
- [ ] **0.7 A port in use.** Type a port another app listens on, such as one a local web server has, and **Save**.
  "Port … is in use by another app. Choose another port, then Save.", in the warm colour. **Save** the default again:
  served.
- [ ] **0.8 The agent can't.** Ask the agent to turn agents off, or to change their port.
  It says those are the user's to change, in ModShelf's Settings, Agents, or with the CLI; `settings.json` is as it was.
- [ ] **0.9 Several processes.** With Claude Desktop, Claude Code and the desktop app running, agents on, look at
  Task Manager's **Details**, and the newest `cli-*.log` files in the library's `logs` folder.
  Several `ModShelf.Cli.exe`: one for the address, and Claude Desktop's (two on Windows) and Claude Code's. Settings ›
  Agents says so under the port. Each one's log starts with "the command line, mcp" or "the command line, mcp --http
  --port --parent", its process ID, and the app that started it, such as `claude` or `ModShelf.Desktop`. Ask the agent
  to rescan the library while you open a game's settings in the app with the gear: the settings open at once.

## 1. The server

- [ ] **1.1 Tools.** Start the agent's app, and ask it which ModShelf tools it has.
  26 tools, from `library_list` to `diagnostics`. Claude Desktop's settings show the server as running. After ModShelf
  is updated, quit Claude Desktop completely, start it again and begin a new conversation: an older list of the tools
  makes calls such as `download` with `link` fail with "download doesn't take these arguments: it needs links. …".
- [ ] **1.2 Reading.** Ask for the library, the updates and the settings; then for the mods with files missing from disk.
  The mods, their files' full paths in the test library, sizes and SHA-256s, and the settings with their defaults marked.
  The missing ones come from one `library_list` with `missingOnly`; with none missing, none.
- [ ] **1.3 Accounts.** Ask which accounts are linked.
  Nexus Mods linked, at its level, such as Premium; never the account's name or key.
- [ ] **1.4 A game by its name.** Ask the agent for the key of "stalker 2", then of "final fantasy 12".
  It calls `mod_games`: S.T.A.L.K.E.R. 2: Heart of Chornobyl first with its key, then Final Fantasy XII: The Zodiac Age.
- [ ] **1.5 What a search can do.** Ask the agent for Stardew Valley's user interface mods updated in the last 30 days,
  then what else it could search them by.
  It calls `mod_search` with `category` "User Interface" (or as you wrote it) and `updatedWithin` 30, and lists only
  those. Its result's `search` names Nexus Mods, its eight orders each both ways, the filters `words`, `author`,
  `category`, `updatedWithin` and `adult`, 100 a page, `endorsements`, and Stardew Valley's categories, which the agent
  can tell you. Asked for a category the game doesn't have, such as Armour: "Stardew Valley has no category 'Armour' on
  Nexus Mods. Its categories: …".

## 2. Downloads

- [ ] **2.1 Without the desktop app.** Quit the desktop app. Ask the agent to download an update from `library_updates`.
  Premium: it says this MCP server downloads it, or, for a file small enough to be done before the call ends, that
  it's downloaded into the library, with `library_changes` for the version it replaced; the file comes into the library; `queue list` in a terminal shows it
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
- [ ] **2.3b The old `link`.** Ask the agent to call `download` with `link` set to one update's link, not `links`.
  It downloads; the result's message starts "Note: 'link' is deprecated and stops working in ModShelf 2.0", and
  `deprecated` says `DL_LINK_ARG`, `removedIn` `2.0`, with the call as it should be made. The log has a warning starting
  `DL_LINK_ARG download called with deprecated 'link'; use 'links'`, with the agent's app's name and without the link.
  With both `link` and `links`: "Pass only 'links'; 'link' is the old name for a single link." Then ask for the
  diagnostics: the last line is "Legacy arguments: download.link ×1 (last …, …)", with the time in UTC and the agent's
  app's name, and `deprecated-calls.json` beside the test library's `settings.json` counts it. Ask again with `link`: ×2.
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
  They match (`Get-FileHash` writes it in capitals). A file from before 0.9.2 has none (`sha256` is null) until
  `library_rescan` reads it.

- [ ] **2.6a What replaces what.** Ask the agent to show Dynamic Menu Overhaul (Dragon's Dogma 2, mod 1641) with all its
  files, or SMAPI (Stardew Valley, mod 2400).
  Each file's `replacedBy` is a file uploaded after it: no newest file says an older, archived one replaces it.

- [ ] **2.7 Pins, skips and notes.** For a watched mod with an update, ask the agent to pin it, then for the updates;
  to unpin it and skip that update, then for the updates again; to offer it again; and to keep a note of the mod.
  The pinned mod's update, then the skipped one, is left out of `library_updates`; `library_list` says `pinned`, the
  note, and the update's `skipped`, and `mod_show` too. A desktop app running meanwhile shows the changes once it starts
  again.

## 3. Settings and refusals

- [ ] **3.1 A setting.** Ask the agent to set `library.oldVersions` to `archive`, then to reset it.
  The desktop app's Settings screen shows each, once it's opened again.
- [ ] **3.1a A game's previews.** With the desktop app running, ask the agent to show Stardew Valley's mods as cards in
  the Library, then open the Settings screen at Stardew Valley's page.
  The agent sets `games.nexus/stardewvalley.previews` to `cards`. Its page shows "Previews" overridden, on **Cards**.
- [ ] **3.1b A game's own folder.** With files of a game in the library, ask the agent to give that game a folder of its
  own. Then move the game's files there yourself with the CLI's `library move-game … --yes`, and ask the agent where its
  files are.
  The agent can't: the server says moving a game's files is for the user, with the CLI's `library move-game`. After the
  move, `library_list` gives the files' paths in the new folder.
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

## 5. GameBanana

GameBanana needs no account. Start a new conversation, so the agent reads the server's instructions afresh.

- [ ] **5.1 Which sites.** Ask the agent where it can find mods for Persona 5 Royal.
  It asks both sites, with `mod_games` and `source` `nexus` and `gamebanana`, and names the site of each game it finds:
  Persona 5 Royal (PC), `16951`, on GameBanana, and Nexus Mods' game of that name, if it has one. GameBanana's are found
  by its own search, by a word of 3 letters or more, in a second or so; asked without words, `mod_games` says GameBanana
  has too many games to list them all.
- [ ] **5.2 A game's key alone.** Ask it to search GameBanana's game 16951 for `protagonist`.
  `mod_search` with `game` `16951`, without `source` or with `gamebanana`; each result's `source` is `gamebanana`, and
  the agent says the mods are GameBanana's, best matches first.
- [ ] **5.3 Adult content.** Ask for the game's most liked mods, 50 of them.
  When GameBanana's first 50 hold mods it shows behind a warning, `leftOut` counts them, the page lists that many fewer,
  and the agent says so, offering to include them.
- [ ] **5.4 A file of a GameBanana mod.** Ask it to download the "Japanese voiceover pack update metadata" file of
  `https://gamebanana.com/mods/440088` into the library.
  It reads the mod with `mod_show`, whose files each have a `https://gamebanana.com/mmdl/<file>,Mod,440088` link, and
  downloads that one, 504 bytes; `library_list` then lists it under game `16951` of `gamebanana`.
- [ ] **5.5 Its updates.** Ask the agent to check the library for updates.
  `library_check` counts the GameBanana mod among those it checked; there's no update for it.
- [ ] **5.6 A linked profile.** Link a GameBanana profile on the desktop app's Accounts screen, then ask the agent which
  accounts are linked.
  `accounts_status` lists GameBanana as linked, with `profile` its way to link one, no level, and `worksWithoutAccount`
  true; the agent never names the profile.

## 6. mod.io

Link a mod.io access token first, on the desktop app's Accounts screen (desktop checklist, 16.8), and start a new
conversation.

- [ ] **6.1 Which sites.** Ask the agent where it can find mods for Baldur's Gate 3.
  It asks the sites with `mod_games` and `source` `nexus`, `gamebanana` and `modio`, and names the site of each game it
  finds: Baldur's Gate 3, `6715`, on mod.io, and Nexus Mods' game of that name.
- [ ] **6.2 A game's key alone.** Ask it to search mod.io's game 6715 for `inventory`.
  `mod_search` with `game` `6715` and `source` `modio` (without `source`, it's told to choose between `gamebanana` and
  `modio`); each result's `source` is `modio`, Better Inventory UI among them, the newest first.
- [ ] **6.3 A file of a mod.io mod.** Ask it to download `https://mod.io/g/baldursgate3/m/better-inventory-ui1` into the
  library, if it isn't there yet.
  It reads the mod with `mod_show`, whose live file has a `https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/…/download`
  link, and downloads it; `library_list` lists it under game `6715` of `modio`.
- [ ] **6.4 The account.** Ask the agent which accounts are linked.
  `accounts_status` lists mod.io as linked, with `api-key` its way to link one, "Access token" its level, and
  `worksWithoutAccount` false; the agent never sees the token.

## 7. DEG Mods

DEG Mods needs no account. Start a new conversation.

- [ ] **7.1 Which sites.** Ask the agent where it can find mods for Fallout: New Vegas.
  It asks the sites with `mod_games`, `degmods` among them, and names the site of each game it finds: Fallout: New Vegas,
  `fallout: new vegas`, on DEG Mods, beside Nexus Mods' game of that name.
- [ ] **7.2 A game's name.** Ask it to search DEG Mods' Fallout: New Vegas for `newspapers`.
  `mod_search` with `game` `fallout: new vegas` (and `source` `degmods`, or without, since a name with a space is DEG
  Mods'); Better Newspapers for New Vegas Bounties II, its `source` `degmods`, its author an `npub1…`.
- [ ] **7.3 A file of a DEG Mods mod.** Ask it to download Better Newspapers into the library.
  It reads the mod with `mod_show`, whose files' links are the mod's page with `#file=…`, and downloads the main one;
  `library_list` lists it under game `fallout: new vegas` of `degmods`.
- [ ] **7.4 One only DEG Mods' server has.** Ask it to download Nipton Rebuilt Remade (the command line's checklist,
  `$nipton`).
  `download` says the file is on a page only the browser downloads it from, and gives the page; the agent says so,
  without Nexus Mods' words about a mod manager download.
- [ ] **7.5 The file from the browser.** On that page, download the file, saving it into the test folder rather than the
  Downloads folder (the browser's "Save as"). Ask the agent to hand the waiting download Better Newspapers' file from the
  library with `downloads_take`, then the saved file by its name alone, then by its full path.
  The first: the agent says it isn't the download's, its checksum not the one the site gives, and the file is still in
  the library; the name alone: "Give the file's full path."; the full path: "Taken: the file is in the library.",
  `library_list` lists Nipton Rebuilt Remade, and the file has left the test folder.

## 8. Games on this PC (Steam)

On a computer with Steam and a few games installed, after the desktop app's checklist, section 18, chose a game for The
Elder Scrolls V: Skyrim Special Edition (or another). Start a new conversation.

- [ ] **8.1 The games here.** Ask the agent which of the games installed on this computer have mods, and on which sites.
  It calls `mod_games` with `installed` `true`: `stores` names Steam, `installed` each game with its name, `steam`, its
  app ID and its games on the sites, each with its key and how it matched (Skyrim's `skyrimspecialedition` on `nexus`,
  chosen), and `problems` any site that couldn't list its games. No folder's path is in the result.
- [ ] **8.2 Then its mods.** Ask it to search the mods of one of those games.
  It calls `mod_search` with the key and site `mod_games` gave, as for any game.

## Results

Copy this table into the release's notes or issue, and mark each check ✅, ❌ or – (not run). For a ❌, add what you
asked, what the agent and the app showed, and the matching lines of the log.

| Check | Result | Notes |
|---|---|---|
| 0.1 Off | | |
| 0.2 On, without a restart | | |
| 0.3 Copy prompt | | |
| 0.4 View details | | |
| 0.5 Add to Claude Desktop | | |
| 0.6 The address | | |
| 0.7 A port in use | | |
| 0.8 The agent can't | | |
| 0.9 Several processes | | |
| 1.1 Tools | | |
| 1.2 Reading | | |
| 1.3 Accounts | | |
| 1.4 A game by its name | | |
| 1.5 What a search can do | | |
| 2.1 Without the desktop app | | |
| 2.2 With the desktop app | | |
| 2.3 Changing a download | | |
| 2.3a Several links | | |
| 2.3b The old `link`, counted | | |
| 2.4 What came in | | |
| 2.5 New optional files | | |
| 2.6 Hashes | | |
| 2.7 Pins, skips and notes | | |
| 3.1 A setting | | |
| 3.1a A game's previews | | |
| 3.1b A game's own folder | | |
| 3.2 What it can't do | | |
| 3.3 The logs | | |
| 3.4 Forgetting a mod | | |
| 3.5 Rescanning | | |
| 4.1 With the app | | |
| 5.1 Which sites | | |
| 5.2 A game's key alone | | |
| 5.3 Adult content | | |
| 5.4 A file of a GameBanana mod | | |
| 5.5 Its updates | | |
| 5.6 A linked profile | | |
| 6.1 Which sites | | |
| 6.2 A game's key alone | | |
| 6.3 A file of a mod.io mod | | |
| 6.4 The account | | |
| 7.1 Which sites | | |
| 7.2 A game's name | | |
| 7.3 A file of a DEG Mods mod | | |
| 7.4 One only DEG Mods' server has | | |
| 7.5 The file from the browser | | |
| 8.1 The games here | | |
| 8.2 Then its mods | | |
