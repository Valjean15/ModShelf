# 96. The games Steam installed, matched to the sites' games

- Status: Accepted
- Date: 2026-11-11

## Context

You asked for your Steam games, to search their mods (Nov 9). Steam's Web API lists the games an account owns only with
a developer's key, which a desktop app can't ship, so the plan (1.1.0, steps 11–12) chose the games installed on this
computer, from the Steam client's own files: no key, no sign-in, no network. Nexus Mods', mod.io's, DEG Mods' and
GameBanana's games name no Steam app IDs, so an installed game is matched to a site's game by its name, as the name
matcher already does (ADR 0065), and by your choice where the names differ ("The Elder Scrolls V: Skyrim Special
Edition" and "Skyrim Special Edition") or two games share one. The shapes were approved on Nov 11.

## Decision

- **The installed games** (Application, `Installed/Ports/`): `IInstalledGames.ReadAsync` gives `InstalledGames`, the
  stores found (`GameStore`: its ID, name and folder) and their games (`InstalledGame`: store, ID, name and folder, its
  `Key` such as `steam/489830`). A store that isn't on this computer gives none; another store (GOG, Epic) can be added
  beside Steam.
- **Steam's files** (Platform, `Installed/`): `SteamFolders` finds Steam where it installs itself: on Windows the
  registry (`SteamPath` for the user, `InstallPath` for the computer) and then `Program Files (x86)\Steam`; on macOS
  `~/Library/Application Support/Steam`; on Linux Flatpak's folder, `$XDG_DATA_HOME/Steam` (or `~/.local/share/Steam`),
  `~/.steam/steam`, `~/.steam/root` and Snap's folder, in that order. `SteamGames` reads, from the first with a
  `steamapps` folder, its library folders (`libraryfolders.vdf`, in today's format and the older one) and each
  library's `appmanifest_<id>.acf`: the app ID, the name and the folder under `steamapps/common`. A game still
  downloading (no "fully installed" flag) and Steam's own tools (Proton, the Steam Linux Runtime, the Steamworks
  redistributables) are left out; the first library to list an app ID wins. `KeyValue` parses Steam's text format,
  with its escapes, comments and conditions, 32 groups deep at most. A file that can't be read is logged and skipped;
  nothing is written to Steam's files, and paths are never logged.
- **The matches** (Application, `Installed/`): `IInstalledGameMatcher.MatchAsync(listed)` gives each installed game its
  games on each site (`SiteMatch`): the game chosen for it on that site, or none chosen, when you chose; otherwise the
  listed games of the same name, by `NameQuery.SameName` (none, one, or several). A chosen game that isn't listed, such
  as GameBanana's, which are found by name, is asked of its site once a run; should the site not answer, its key stands
  for its name. `PickAsync(game, site, picked)` keeps a choice (null for "none here"), `UnpickAsync` forgets it, so the
  name decides again.
- **Kept in `settings.json`**: `AppSettings.InstalledMatches`, as `"installedGames": { "steam/489830": { "nexus":
  "skyrimspecialedition", "modio": null } }`, sorted, a game with no choice left dropped; a wrong entry is refused
  saying which, as the file's other sections are. Not a `settings set` key: the choices are made where the games show.
- **Browse's game picker** lists **On this PC (Steam)** first: each installed game's games on the sites, those with a
  match only, each once however many installed games match it, and narrowed to the site chosen in the box. A name two
  games of a site share lists both, each saying its key ("…, one of the games of this name"), and choosing one keeps it
  for that installed game. Typing finds them by their names or the installed game's. They're matched again each time
  Browse shows, after the sites' games are read.
- **Settings › Games** shows **Games on this PC** above the games' own settings: where the games were found ("Steam: 5
  games installed, 4 with a site's game"), each installed game with its games on the sites ("Nexus Mods: Skyrim Special
  Edition, chosen · DEG Mods: none, chosen"), and **Choose…**, which opens a dialog listing the sites with a game or a
  choice (**Match by name** to forget a choice, **None here** to say a site has none), and the game picker over every
  site's games to choose another. The installed games show at once, matched again as each site's games come; a site
  that can't list its games is said, its games then matching only as chosen.
- **The command line and the MCP server**: `mod games --installed [words] [--source]` lists the installed games, a row
  for each game on each site, with how it matched ("same name", "one of the same name", "chosen", "none, chosen");
  `mod_games` with `installed: true` gives the same as `stores`, `installed` and `problems`. Both read the sites in
  parallel. Choosing stays in the desktop app.
- Log events 2200–2299 (Platform's Steam files and Application's matcher) are a new block; Presentation's are 971–975.

## Consequences

- Browse's picker starts with the games you have, on the site chosen; a game installed under another name is chosen
  once and kept.
- Only installed games, and only Steam's, are known; uninstalling a game drops it from the lists, its choices kept in
  `settings.json` for when it's back.
- A new public port and matcher in Application (approved); no package, no database change, nothing sent anywhere.

## Testing

- Platform: `KeyValue` (strings, escapes, comments, conditions, groups too deep, files cut short), `SteamFolders` on
  Windows (the registry, a key that can't be read, each folder once) and Linux (Flatpak, XDG, Snap), `SteamGames` over
  Steam folders written in the tests (two libraries, the old format, games downloading, tools, broken or incomplete
  manifests, a library folder gone, no path logged).
- Application: the matcher (by name, several of a name, chosen, none chosen, chosen and not listed, a site that can't
  say its game, forgetting), the settings kept and forgotten; Infrastructure: `installedGames` read, written and refused.
- Presentation: the picker's games on this PC (first, once, by either name, narrowed by the box, chosen among a shared
  name), Browse matching again as it shows and keeping a choice, Settings' card and dialog (a slow site keeping none
  waiting, problems in the sites' order, choices kept or not).
- CLI and MCP: the installed games listed, by words (theirs or their games'), one site's alone, a page at a time,
  without Steam, with a site that can't list its games.
- 104 deliberate bugs: 93 caught at once; 2 refused by the compiler, redefined and caught; of 9 that survived, one was
  equivalent (a site's game chosen is always alone), so the code was simplified, one was caught by the MCP server's
  tests once they ran too, and seven led to tests (a folder the registry writes in another case, a backslash in a manifest's folder,
  sites picked alone in order, a chosen game's dialog, the dialog finding GameBanana's games, `--source`, and words
  that find a game by its site's game).
- By hand: a Steam folder made up of five games, a game downloading and Proton, in two libraries, on Linux under Xvfb:
  Settings › Games' card and dialog, and Browse's picker on DEG Mods (desktop checklist, section 18).
