# Manual checklist

CI never calls a live mod site, so before each release someone runs these checks by hand against Nexus Mods: once with a **Premium** account and once with a **free** one. Section 13 checks GameBanana, once, without an account and then with a profile linked; section 14 checks mod.io, once, with an API key and then with an access token; section 15 checks DEG Mods, once, without an account. Phase 3 is done when they all pass.

The checks use the CLI. They download files into a temporary folder, and keep the library database and credentials apart from your own. One section changes your account: it tracks SMAPI and untracks it again, leaving your tracked mods as they were.

Each check gives a command, then what you should see. Quoted text is what the CLI prints. The **exit code** is in `$LASTEXITCODE` after each command: 0 done, 1 failed or refused, 2 bad input, 3 still waiting, 130 stopped with Ctrl+C.

## Before you start

You need:

- the personal API keys of a Premium account and a free account, from [your API keys page](https://next.nexusmods.com/settings/api-keys);
- a browser signed in to Nexus Mods with the **free** account, for the handoff checks;
- PowerShell 7 (`pwsh`), from the repository root.

If Vortex or Mod Organizer 2 is set to handle `nxm://` links, it takes the link the website hands over in section 5. Turn that off in its settings for the checklist, or copy the link from the browser as the README describes.

Set up a folder of its own for the checks, build, and give the CLI a short name:

```powershell
$test = Join-Path ([System.IO.Path]::GetTempPath()) 'ModShelf-checklist'
$env:ModShelf__DatabasePath = Join-Path $test 'library.db'
$env:ModShelf__SecretsFolder = Join-Path $test 'Secrets'
$env:ModShelf__SecretScope = 'ModShelf.Checklist'
dotnet build
$cli = Resolve-Path src/ModShelf.Cli/bin/Debug/net10.0/ModShelf.Cli.dll
function ms { dotnet $cli @args }
```

Run sections 1 to 12 with the Premium account first. Then run them again with the free account, skipping the checks marked **Premium only**. Run section 13 once, in the Premium run, before section 12. Each account downloads into a folder of its own, so the second run doesn't find the first one's files:

```powershell
$downloads = Join-Path $test 'Premium'   # 'Free' for the free account
```

A new terminal needs all these lines again, with the same folders. Never paste an API key or an `nxm://` link into a report: both carry keys.

## 1. Link the account

- [ ] **1.1 A wrong key is refused.** `ms account link nexus`, then type `not-a-real-key`.
  "nexus refused the API key. Check it, then try again. Nothing was linked." Exit code 1.
- [ ] **1.2 The key links the account.** `ms account link nexus`, then paste the key.
  The key isn't shown as you type or paste it. "Linked nexus as *your name* (Premium)", or "(Free)" for the free account. Exit code 0. On Windows, the test folder's `Secrets` folder now holds `account_nexus.secret`.
- [ ] **1.3 The account is listed.** `ms account list`.
  The `nexus` row shows your name, the account's level and today's date.

## 2. Look a mod up

- [ ] **2.1 A mod's page.** `ms mod show "https://www.nexusmods.com/stardewvalley/mods/2400"`.
  SMAPI's name, game, author, version, last update, endorsements, downloads and page. Then its main file, marked `*`, with a size and a link. It ends with a line saying how many old files aren't shown, and how to download a file. The details match the mod's page on the website.
- [ ] **2.2 Old files and the changelog.** The same command with `--all --changelog`.
  The old versions and the file in no section are listed too, then the changelog, or "The mod has no changelog."
- [ ] **2.3 A file's link.** Copy one old version's link from 2.2 and run `ms mod show "<that link>"`.
  That file is marked `*`, and its section is listed without `--all`.

## 3. Search

- [ ] **3.1 Words.** `ms mod search smapi --game stardewvalley`.
  "*N* mods match, best matches first. These are 1 to 20." SMAPI is near the top. Every row has a link that `mod show` takes.
- [ ] **3.2 Order and page size.** `ms mod search --game stardewvalley --sort Newest --ascending --page-size 50`.
  "…, oldest first. These are 1 to 50." The first mods are the game's oldest on the website.
- [ ] **3.3 Author and next page.** `ms mod search --game stardewvalley --author Pathoschild --page 2 --page-size 5`.
  Only Pathoschild's mods, numbered from 6.
- [ ] **3.4 No match.** `ms mod search zqxj no such mod zqxj --game stardewvalley`.
  "No mods match." Exit code 0.
- [ ] **3.4a Games by name.** `ms mod games stalker 2 --source nexus`, then `ms mod games final fantasy 12 --source nexus`, then `ms mod games valley stardew --source nexus`; then `ms mod games stalker 2`.
  S.T.A.L.K.E.R. 2: Heart of Chornobyl, with its key; Final Fantasy XII: The Zodiac Age; Stardew Valley, key `stardewvalley`. Each says how many match, and "To search a game's mods: mod search --game <its key>". Without `--source`, "More than one site can list its games; choose one with --source: nexus, gamebanana.", exit code 2.
- [ ] **3.5 The site's own order, and endorsements.** `ms mod search --game stardewvalley`, then the same with `--sort Endorsements`.
  "*N* mods match, in the site's own order. These are 1 to 20.", the most endorsed at the top, then "…, most endorsements first." with the same mods. The table's columns are ID, Name, Author, Endorsements, Downloads, Updated and Link.
- [ ] **3.6 A category, and how long since updated.** `ms mod search --game stardewvalley --category "user interface" --updated-within 30`, then `ms mod search --game stardewvalley --category Armour`.
  Only User Interface mods, each updated in the last 30 days (the Updated column). Then "Stardew Valley has no category 'Armour' on Nexus Mods. Its categories: …", listing the game's categories as the website's mod categories for Stardew Valley. Exit code 2.

## 4. Download straight away (Premium only)

- [ ] **4.1 A mod's page.** `ms download "https://www.nexusmods.com/stardewvalley/mods/2400" --to $downloads`.
  "Downloading *SMAPI's main file* to …", progress, then "Saved …". Exit code 0. The file's size matches the Files tab, and it opens as an archive.
- [ ] **4.2 A file's link.** `ms download "<an old version's link from 2.2>" --to $downloads`.
  Saved under that file's own name.
- [ ] **4.3 The queue.** `ms queue list`.
  Both downloads, completed.

## 5. Download through the website (free only)

- [ ] **5.1 The page opens.** `ms download "https://www.nexusmods.com/stardewvalley/mods/2400" --to $downloads`.
  "The site wants this download started on its website." The file's page opens in your browser, on the Files tab, and its address is printed too. Then "Link: " waits.
- [ ] **5.2 A line that isn't a link.** Type `hello` and press Enter.
  "That isn't a link. Paste the link the site handed over, or press Ctrl+C to stop."
- [ ] **5.3 Ctrl+C leaves it waiting.** Press Ctrl+C.
  "Stopped. The download is still waiting to be started on the site; run the same command again to continue." Exit code 130.
- [ ] **5.4 Running it again shows the page again.** Run the command from 5.1 again.
  "Continuing the download of … added earlier.", and the page opens again.
- [ ] **5.5 The handed-over link finishes it.** On the page, click **Slow download**. Copy the `nxm://` link the browser was handed, paste it at "Link: " and press Enter.
  Progress, then "Saved …". Exit code 0.
- [ ] **5.6 The link alone.** Run 5.1 with a new folder, `--to (Join-Path $downloads alone)`, and press Ctrl+C at "Link: ". Start the download on the website again, then run `ms download "<the new nxm:// link>" --to (Join-Path $downloads alone)`.
  "Continuing the download of … added earlier.", then "Saved …", without asking for anything.
- [ ] **5.7 A link to another file.** Run 5.1 with another new folder, `--to (Join-Path $downloads other)`. On the website, start the download of a *different* file of SMAPI, and paste its `nxm://` link.
  "That link is to another file, *its name*. Paste the link for *the first file*, or press Ctrl+C to stop." Press Ctrl+C.
- [ ] **5.8 The file's page isn't the link handed over.** Run 5.1 with another new folder, `--to (Join-Path $downloads page)`. At "Link: ", paste the file's page address printed above it, and press Enter.
  "That's a link to *the file's name*, but not the one the site hands over: start the download on the page above, then paste the link its download button holds, or press Ctrl+C to stop." Press Ctrl+C.
- [ ] **5.9 Prompts with the output in a file.** `ms download "https://www.nexusmods.com/stardewvalley/mods/2400" --to (Join-Path $downloads page) > (Join-Path $test output.txt)`.
  The page's address and "Link: " still show in the terminal. Press Ctrl+C; `output.txt` holds no "Link: ".

## 6. Stop and continue

Use a file of a few hundred MB, so there's time to stop it: for example a texture pack's main file. Copy its link from the mod's Files tab.

- [ ] **6.1 Ctrl+C keeps the partial file.** `ms download "<the file's link>" --to $downloads`. On the free account, start it on the website as in 5.5. Press Ctrl+C at about a third.
  "Stopped. The partly downloaded file is kept; run the same command again to continue." Exit code 130. The folder holds a `.part` file.
- [ ] **6.2 It continues where it stopped.** Run the same command again. On the free account, it asks for a new link: start the download on the website again and paste it.
  "Continuing the download of … added earlier." The progress starts at about a third, not at zero, and ends with "Saved …".
- [ ] **6.3 It survives a crash** (Premium only). Start another large file, and close the terminal window halfway. Open a new terminal, run the setup lines again, then the same command.
  It continues from where it was, and ends with "Saved …".

## 7. Updates

- [ ] **7.1 The last day.** `ms mod updates --game stardewvalley`.
  "*N* mods of stardewvalley changed since *yesterday at this time*, the most recently active first." Rows with an ID, two times and a link; mods with new files are marked `*`. Open one marked mod's link: its Files tab shows a file uploaded at about the time given.
- [ ] **7.2 Longer.** The same with `--since 7d`, then `--since 28d`.
  Exit code 0 both times, with more mods.
- [ ] **7.3 Too far back.** The same with `--since (Get-Date).AddDays(-40).ToString('yyyy-MM-dd')`.
  "Nexus Mods lists the mods updated in the last 28 days only." Exit code 2.
- [ ] **7.4 A game Nexus Mods doesn't have.** `ms mod updates --game notagame`.
  Write down what it says. "The site has no game 'notagame'." is expected, but Nexus Mods doesn't document this answer.
- [ ] **7.5 A game's own settings.** `ms library watch "https://www.nexusmods.com/stardewvalley/mods/2400"`, then `ms settings set games.stardewvalley.checkUpdates false` and `ms library check`. Then `ms settings set games.nexus/stardewvalley.updateInterval 1d`, `ms settings set games.nexus/stardewvalley.browse.window 7d`, `ms settings show`, and `ms settings reset` for each of the three. Last, `ms library unwatch "https://www.nexusmods.com/stardewvalley/mods/2400"`.
  The first set says `games.nexus/stardewvalley.checkUpdates is now false`, the game's key alone naming Nexus Mods' game. With `checkUpdates` false, `library check` says "Checked 0 mods with 0 requests." `settings show` lists `games.nexus/stardewvalley.checkUpdates false`, `games.nexus/stardewvalley.updateInterval 1d` and `games.nexus/stardewvalley.browse.window 7d`, and `settings.json` holds them under `"games"`, as `"nexus/stardewvalley"`. Each reset says the game goes by every game's setting again, or Browse's own, and `settings show` lists none of them afterwards.
- [ ] **7.5a A game's previews.** `ms settings set games.stardewvalley.previews cards`, `ms settings set games.stardewvalley.previews big`, `ms settings show`, then `ms settings reset games.stardewvalley.previews`.
  The first says "games.nexus/stardewvalley.previews is now cards: on nexus/stardewvalley's page in the desktop app's Library, and in Browse while it's chosen, its mods show as cards, with large pictures." The second says "'big' isn't off, thumbnails or cards." with exit code 2. `settings show` lists `games.nexus/stardewvalley.previews cards`, and `settings.json` holds `"previews": "cards"` under `"nexus/stardewvalley"`. The reset says the game's mods show as `previews.library` and `previews.browse` say.
- [ ] **7.5b A game's own folder.** With SMAPI's file downloaded (4 or 5): `ms settings set games.stardewvalley.root "$test\Stardew"`, then `ms library move-game stardewvalley "$test\Stardew"`, then the same with `--yes`, `ms library list --files` and `ms settings show`. Then `ms download "https://www.nexusmods.com/stardewvalley/mods/2400"` once more, and last `ms library move-game stardewvalley --yes`.
  The set is refused (exit code 1): "nexus/stardewvalley has files in …. Moving the game's files changes its folder and takes them along. To move them there: library move-game stardewvalley "…\Stardew"". Without `--yes`, it says how many files move, how much room they take, from where to where, and that both folders are on one drive; "Nothing moved yet. To move them: add --yes." and nothing moves. With `--yes`: "Moved 1 file from … to …\Stardew." and the file is under `…\Stardew\nexus\stardewvalley\SMAPI (2400)\`. `library list --files` shows it there, `settings show` lists `games.nexus/stardewvalley.root`, and the download says the file is in the library already, at its new path. Moving back puts the file in the library's folder, removes the emptied `Stardew` folder, and `settings show` no longer lists the root.
- [ ] **7.6 Settings from before 1.0.0.** Before upgrading, note a game's own setting in `settings.json`, such as `"stardewvalley": { "notify": false }` under `"games"`. After upgrading, `ms settings show`.
  It lists `games.nexus/stardewvalley.notify false`. After any `ms settings set`, `settings.json` names the game `"nexus/stardewvalley"`, and keeps anything else it held.

## 8. Tracked mods (Premium only)

This section changes your account. If SMAPI is tracked when you start, track it again at the end.

- [ ] **8.1 The list.** `ms mod tracked`.
  "*N* tracked mods on Nexus Mods.", by game and then by number, and "To untrack one: mod untrack "<its link>"". It matches the Tracking Centre on the website.
- [ ] **8.2 Track.** `ms mod track "https://www.nexusmods.com/stardewvalley/mods/2400"`.
  "Tracked on Nexus Mods: mod 2400 of stardewvalley." Exit code 0. `ms mod tracked` lists `stardewvalley 2400`, and the website shows SMAPI as tracked.
- [ ] **8.3 Track again.** The same command.
  The same message, exit code 0.
- [ ] **8.4 Untrack.** `ms mod untrack "https://www.nexusmods.com/stardewvalley/mods/2400"`.
  "No longer tracked on Nexus Mods: mod 2400 of stardewvalley." It's gone from `ms mod tracked` and from the website.
- [ ] **8.5 Untrack again.** The same command.
  The same message, exit code 0.

## 9. Refusals

- [ ] **9.1 A mod that doesn't exist.** `ms mod show "https://www.nexusmods.com/stardewvalley/mods/999999999"`.
  "The site has no such mod or file." Exit code 2.
- [ ] **9.2 A collection.** `ms mod show "https://next.nexusmods.com/stardewvalley/collections/abcdef"`.
  "ModShelf downloads single files, not collections." Exit code 2.
- [ ] **9.3 A Nexus Mods page that isn't a mod.** `ms download "https://www.nexusmods.com/stardewvalley/news" --to $downloads`.
  "That Nexus Mods link isn't to a mod. Copy the link of the mod's page, or of a file on its Files tab." Exit code 2.
- [ ] **9.4 A link to Nexus Mods' file servers.** `ms download "https://cf-files.nexusmods.com/cdn/1704/2400/SMAPI.zip" --to $downloads`.
  "That's a link to Nexus Mods' servers, not to a page: a download link from them stops working after a while. Copy the mod's page link, or the file's link on its Files tab, instead." Exit code 2.

## 10. Nothing secret in the logs

- [ ] **10.1 Trace logs.** Run a command with every log turned on, keeping the log in a file:

  ```powershell
  $log = Join-Path $test 'trace.log'
  $env:Logging__LogLevel__Default = 'Trace'
  ms mod show "https://www.nexusmods.com/stardewvalley/mods/2400" 2> $log > $null
  Remove-Item Env:Logging__LogLevel__Default
  Select-String -Path $log -Pattern 'apikey', 'key='
  ```

  Every line found reads `apikey: *`: the log names the header but never shows its value. No line holds `key=`.
- [ ] **10.2 With a handed-over link** (free only). The same, with a download that asks for a link; start it on the website and paste the `nxm://` link when asked:

  ```powershell
  $env:Logging__LogLevel__Default = 'Trace'
  ms download "https://www.nexusmods.com/stardewvalley/mods/2400" --to (Join-Path $downloads traced) 2> $log
  Remove-Item Env:Logging__LogLevel__Default
  Select-String -Path $log -Pattern 'apikey', 'key='
  ```

  The same: only `apikey: *` lines, and no `key=` anywhere. Addresses with a query show `?*` in its place.
- [ ] **10.3 The diagnostics report.** Write the report a bug report needs:

  ```powershell
  ms diagnostics
  ```

  The first line is "ModShelf 0.9.0-alpha.0.N (…)", where the part in brackets is the commit you built, as `git rev-parse --short=7 HEAD` shows it. Then the system, the folders with your home folder as `~` (and ", 1 game in folders of their own" after 7.5b, while a game has one), the settings, the library's counts, the downloads by what they're doing, "nexus linked (ApiKey, Premium)" (Free for the free account), and last "Legacy arguments: none", or the deprecated calls an agent made (the MCP checklist's 2.3b). Nowhere does it hold your account's name, a mod's or file's name, a key or an `nxm://` link. Exit code 0.

## 11. Links from websites

These checks make the desktop app open Nexus Mods' `nxm://` links, from the command line, then give them back. Skip them on macOS: only the installed app can take the links there.

- [ ] **11.1 Who opens them.** Point the CLI at the desktop app it was built with, then ask:

  ```powershell
  $env:ModShelf__DesktopApp = Resolve-Path src/ModShelf.Desktop/bin/Debug/net10.0/ModShelf.Desktop.exe   # without .exe on Linux
  ms links status
  ```

  A table with `nexus`, `nxm://` and the app that opens them, such as `Vortex`, or `no app`. Exit code 0.
- [ ] **11.2 Take them.** `ms links take`.
  "The desktop app opens nxm:// links from Nexus Mods now. Vortex opened them before; links give-back nexus gives them back to it." (or "… now. links give-back nexus stops it." when no app had them). `ms links status` shows `ModShelf (before it, Vortex)`.
- [ ] **11.3 A click opens the desktop app.** On SMAPI's Files tab on the website, click **Mod manager download** on its main file (with the free account, then **Slow download**).
  The desktop app starts on the Downloads screen, with the test folder's library, and the download ends "Done". Quit it from its tray menu.
- [ ] **11.4 Give them back.** `ms links give-back`.
  "ModShelf no longer opens nxm:// links from Nexus Mods. Vortex opens them now." (or "No app opens them now."). `ms links status` shows what 11.1 did.
- [ ] **11.5 An unknown site.** `ms links take moddb`.
  "No site 'moddb' hands its downloads over with links: links status lists them." Exit code 2.

## 12. Unlink the account

- [ ] **12.1 Unlink.** `ms account unlink nexus`.
  "Unlinked nexus. Its stored credentials are deleted." On Windows, `account_nexus.secret` is gone from the test folder's `Secrets` folder.
- [ ] **12.2 Listed as not linked.** `ms account list`.
  The `nexus` row shows `-` for the account.
- [ ] **12.3 The site now needs an account.** `ms mod show "https://www.nexusmods.com/stardewvalley/mods/2400"`.
  "The site needs a linked account to look that link up, or refused the one linked. Link it with account link, then run the same command again." Exit code 1.
- [ ] **12.4 Search without an account.** `ms mod search smapi --game stardewvalley`.
  Write down what happens: Nexus Mods doesn't say whether its search needs an account. Either results or "The site needs a linked account to search…" is fine.

## 13. GameBanana

GameBanana needs no account; 13.13 to 13.17 link a member's public profile, read only: yours, or, without a GameBanana account, `chlorophylls`, the mod's author. The checks use P5R Female Protagonist (mod 440088) of Persona 5 Royal (PC) (game 16951), and download only its small metadata files, of a few hundred bytes:

```powershell
$gb = Join-Path $test 'GameBanana'
```

- [ ] **13.1 A mod's page.** `ms mod show "https://gamebanana.com/mods/440088"`.
  "P5R Female Protagonist", "Game: Persona 5 Royal (PC)", its author, "Likes:" and "Downloads:", and its page. Under "Main files", the mod and its voiceover packs, the newest marked `*`; under "Miscellaneous files", their metadata files of a few hundred bytes. Each file's link is `https://gamebanana.com/mmdl/<file>,Mod,440088`. "1 more file (an old version, archived or in no section) isn't shown". Exit code 0.
- [ ] **13.2 Old versions and the changelog.** The same with `--all --changelog`.
  An "Old versions" section with the archived version, then the changelog, newest version first, as the mod's Updates tab on the website lists it.
- [ ] **13.3 Games by name.** `ms mod games persona 5 --source gamebanana`, timing it: `Measure-Command { ms mod games persona 5 --source gamebanana | Out-Host }`; then `ms mod games --source gamebanana` and `ms mod games p5 --source gamebanana`.
  "10 games on GameBanana match.", `16951 Persona 5 Royal (PC)` among them, in a second or so: GameBanana's own search finds them by "persona", never its whole list of games. Then "GameBanana has too many games to list them all: give words of the game's name." and "GameBanana finds games by a word of 3 letters or more.", exit code 2 for each.
- [ ] **13.4 A game's mods, and a game's key alone.** `ms mod search --game 16951 --page-size 5`, then `ms mod search --game 16951 --source gamebanana --page-size 5`.
  "More than one site can search; choose one with --source: gamebanana, modio.", exit code 2: a number could be GameBanana's or mod.io's, and never Nexus Mods', though a Nexus Mods account is linked. Then "*N* mods match, in the site's own order." The newest mods of the game on the website first. The Downloads column is `-`: GameBanana's lists don't say.
- [ ] **13.5 Words, author and category.** `ms mod search protagonist --game 16951 --source gamebanana --page-size 20`, `ms mod search --game 16951 --source gamebanana --author chlorophylls`, then `ms mod search --game 16951 --source gamebanana --category models --page-size 5`.
  "best matches first", as the website's search for the words in the game; then the author's mods, P5R Female Protagonist among them; then only Models mods.
- [ ] **13.6 Adult content.** `ms mod search --game 16951 --source gamebanana --sort Endorsements --page-size 50`, then the same with `--adult`.
  "most likes first". When the first 50 hold mods the website shows behind a warning, the first says "…, but *N* with adult content are left out (add --adult to see them)." and lists 50 less *N*; with `--adult`, 50, those included.
- [ ] **13.7 What GameBanana can't search.** `ms mod search --game 16951 --source gamebanana --updated-within 7`, then `ms mod search protagonist --game 16951 --source gamebanana --sort Name`.
  "GameBanana can't search by when mods were updated. It can search by words, by author and by category." Then "GameBanana sorts a search by words by best match, popularity, newest or recently updated only." Exit code 2 for each.
- [ ] **13.8 Download a file by its link.** `ms download "https://gamebanana.com/mmdl/1662079,Mod,440088" --to $gb`.
  "Checking the file", "Done", and `p5rpcfemaleprotagonist-jpnvoreleasemetadata.json`, 504 bytes, in the folder. Exit code 0.
- [ ] **13.9 A 1-click link behind a mod manager's scheme.** `ms download "jasm-plus:https://gamebanana.com/mmdl/1662077,Mod,440088" --to $gb`.
  The same, with `p5rpcfemaleprotagonist-engvoreleasemetadata.json`, 500 bytes.
- [ ] **13.10 Into the library, and its updates.** `ms download "https://gamebanana.com/mmdl/1722099,Mod,440088"`, then `ms library list --files`, `ms library check` and `ms library updates`.
  "Added to the library."; the file under `gamebanana/16951/P5R Female Protagonist (440088)` in the library's folder, its mod watched; the check counts the GameBanana mod among those it checked, and `library updates` lists no update of it.
- [ ] **13.11 Refusals.** `ms download "https://gamebanana.com/mmdl/1662079" --to $gb`, `ms mod show "https://gamebanana.com/games/16951"`, then `ms mod show "https://gamebanana.com/mods/999999999"`.
  "This GameBanana link names a file but not its mod, so ModShelf can't tell whose it is. Copy the mod's page link instead."; "This GameBanana link isn't to a mod or its files. Copy a mod's page link."; "The site has no such mod or file." Exit code 2 for each.
- [ ] **13.12 Forget it.** `ms library forget "https://gamebanana.com/mods/440088"`.
  The mod leaves the library; its file stays on disk.
- [ ] **13.13 Link a profile.** `ms account list`, then `ms account link gamebanana`, and type the member name; then `ms account list` again.
  `gamebanana  - (optional)` with `profile` under "Link with"; "ModShelf reads only what anyone can see on your profile on gamebanana, and asks for no password.", no password asked, then "Linked gamebanana as *name*." Listed again, the name, no level, and today's date. Exit code 0.
- [ ] **13.14 By its link, and refusals.** `ms account link gamebanana --with profile`, pasting the profile's link from the website (`https://gamebanana.com/members/<number>`); then again with `nobody-zz-qq`, then with `https://gamebanana.com/mods/440088`.
  "Linked gamebanana as *name*." Then twice "gamebanana has no such member, or doesn't show their profile. Check the name, then try again. Nothing was linked.", exit code 1, the profile linked first still linked.
- [ ] **13.15 Subscribed mods.** `ms mod tracked --source gamebanana`.
  "*N* subscribed mods on GameBanana.", each mod's game and ID, as the profile's subscriptions on the website list its mods (works in progress and the rest left out), then "ModShelf only reads them: change them on GameBanana's website." A profile subscribed to none: "No subscribed mods on GameBanana."
- [ ] **13.16 Not changed here.** `ms mod track "https://gamebanana.com/mods/440088"`, then `ms mod untrack` with the same link.
  "ModShelf can only read your subscribed mods on GameBanana: change them on its website.", exit code 2 for each; the website's subscriptions unchanged.
- [ ] **13.17 Unlink it.** `ms account unlink gamebanana`, then `ms mod tracked --source gamebanana`.
  "Unlinked gamebanana. Its stored credentials are deleted." Then "GameBanana needs a linked account to list its subscribed mods, or refused the one linked. Link it with account link, then run the same command again.", exit code 1. Everything else on GameBanana works as before.

## 14. mod.io

mod.io answers nothing without a credential. On [your API Access page](https://mod.io/me/access), copy your **API key**, and make a personal **access token** with the `read` scope; revoke the token once the checks are done. The checks use Baldur's Gate 3 (game 6715) and Better Inventory UI (mod 4228735), whose live file is about 460 KB. Subscribe to Better Inventory UI on the website before 14.12; ModShelf itself never subscribes.

```powershell
$mio = Join-Path $test 'modio'
```

- [ ] **14.1 Nothing without a credential.** `ms mod games baldur --source modio`.
  "The site needs a linked account to list its games, or refused the one linked. …", exit code 1.
- [ ] **14.2 A wrong key is refused.** `ms account link modio`, then type `not-a-real-key`; then again with 32 zeros (`00000000000000000000000000000000`).
  "Find your API key for modio at https://mod.io/me/access" first. `not-a-real-key` is refused without asking mod.io: "modio refused the API key. Check it, then try again. Nothing was linked.", exit code 1; the zeros, shaped as a key, are asked of mod.io, which refuses them the same way.
- [ ] **14.3 The API key links.** `ms account link modio`, paste the API key; then `ms account list`.
  The key isn't shown. "Linked modio as API key (Without subscriptions)." The `modio` row shows "API key", "Without subscriptions" and today's date, with `api-key` under "Link with".
- [ ] **14.4 Games.** `ms mod games baldur --source modio`, then `ms mod games --source modio --page-size 100`.
  "Baldur's Gate 3" with the key `6715`; then about 390 games, open to apps such as ModShelf, in four requests.
- [ ] **14.5 A mod's page.** `ms mod show "https://mod.io/g/baldursgate3/m/better-inventory-ui1"`, then the same with `--all --changelog`.
  "Better Inventory UI", "Game: Baldur's Gate 3", "by Caites", its positive ratings and downloads, and its page. Under "Main files", its live file, version 1.4.4.21 as on the website, marked `*`, its link `https://g-6715.modapi.io/v1/games/6715/mods/4228735/files/<file>/download`. With `--all`, its earlier versions under "Old versions", each the website's earlier file for Windows; the changelog, if the author wrote one.
- [ ] **14.6 A game's mods, and a game's key alone.** `ms mod search --game 6715 --page-size 5`, then `ms mod search --game 6715 --source modio --page-size 5`.
  "More than one site can search; choose one with --source: gamebanana, modio.", exit code 2. Then "*N* mods match", the newest first, as the game's mods on the website sorted by "Recent".
- [ ] **14.7 Words, author, tag, time and order.** `ms mod search better --game 6715 --source modio`, `ms mod search --game 6715 --source modio --author Caites`, `ms mod search --game 6715 --source modio --category "quality of life"`, `ms mod search --game 6715 --source modio --updated-within 7 --sort Updated`, then `ms mod search --game 6715 --source modio --sort Downloads --page-size 5`.
  Mods with "better" in their names; Caites' mods, Better Inventory UI among them; mods tagged Quality of Life; mods updated in the last week, the latest first; the most downloaded, with their downloads. `--sort Endorsements` is refused, naming the orders mod.io has.
- [ ] **14.8 Adult content.** `ms mod search --game 6715 --source modio --page-size 50`, then the same with `--adult`.
  When the newest 50 hold mods the website marks explicit, the first says "…, but *N* with adult content are left out (add --adult to see them)." and lists 50 less *N*; with `--adult`, 50.
- [ ] **14.9 Download a file by its link.** `ms download "https://mod.io/g/baldursgate3/m/better-inventory-ui1" --to $mio`.
  "Checking the file", "Done", and `betterinventoryui_<…>.zip`, the size the website gives the live file, in the folder; its MD5 checked. Exit code 0.
- [ ] **14.10 Into the library, and its update.** From 14.5's `--all`, copy the link of the version before the live one: `ms download "<that link>"`, then `ms library check` and `ms library updates`.
  "Added to the library."; the file under `modio/6715/Better Inventory UI (4228735)`; the check finds the live file as its update, with its link.
- [ ] **14.11 Subscriptions need a token.** `ms mod tracked --source modio`; then `ms account list`.
  "mod.io couldn't list the subscribed mods: mod.io lists them for an access token only: link one in place of your API key.", exit code 1. The key is still linked, not waiting to be linked again.
- [ ] **14.12 A token links, with subscriptions.** `ms account link modio`, paste the token; then `ms mod tracked --source modio`.
  "Linked modio as *your name* (Access token)." Then "1 subscribed mod on mod.io." with Better Inventory UI, of game 6715, as your subscriptions on the website, then "ModShelf only reads them: change them on mod.io's website."
- [ ] **14.13 Not changed here.** `ms mod track "https://mod.io/g/baldursgate3/m/better-inventory-ui1"`, then `ms mod untrack` with the same link.
  "ModShelf can only read your subscribed mods on mod.io: change them on its website.", exit code 2 for each; the website's subscriptions unchanged.
- [ ] **14.14 Updates.** `ms mod updates --game 6715 --source modio --since 7d`.
  The mods whose live file changed in the last week, each with when, in one or a few requests.
- [ ] **14.15 Refusals.** `ms mod show "https://mod.io/g/baldursgate3"`, `ms mod show "https://mod.io/g/baldursgate3/m/no-such-mod-zz-qq"`, then `ms mod show "https://mod.io/g/no-such-game-zz-qq/m/better-inventory-ui1"`.
  "That mod.io link isn't to a mod: copy the link of a mod's page."; then that mod.io has no such mod of the game, perhaps renamed; then no such game. Exit code 2 for each.
- [ ] **14.16 Nothing secret in the logs.** `Select-String -Path (Join-Path $test 'logs\*') -Pattern '<the first 12 characters of the key>', '<the first 12 characters of the token>'`.
  Nothing found. The logs name mod.io's addresses without their queries (`?*`), and requests' headers without their values.
- [ ] **14.17 Unlink it.** `ms account unlink modio`, then `ms mod search --game 6715 --source modio`.
  "Unlinked modio. Its stored credentials are deleted." Then the search says the site needs a linked account, exit code 1. Revoke the token on mod.io.

## 15. DEG Mods

DEG Mods needs no account: its mods are Nostr events on its relays, and its files are on Blossom servers, named by their SHA-256. The checks use Fallout: New Vegas' Better Newspapers for New Vegas Bounties II (three files, the first on DEG Mods' own server and another too), Nipton Rebuilt Remade (files only DEG Mods' own server has, and two on the Internet Archive), Starfield's VGB Fed and Hydration Duration Increase (an older listing, on catbox.moe), Halo & Doom OSTs (on MediaFire), Standalone Driveable Cars v7.11 (on RPGHQ's file host, 470 MB), Street Fighter 6's Reject Wifi (two versions), and a test post DEG Mods' admin blocked.

```powershell
$deg = Join-Path $test 'degmods'
$newspapers = 'https://degmods.com/mod/naddr1qqjrwcmrv5enqv3395mr2vf5956rwe3h94snqdf594snywryvvcnyd3evenrgq3qzm3rm4rd3aqnk65e8l642kzp6xkhdh67g4rk4k5ytyl4a4xpuxcsxpqqqpu6vwmyaq6'
$nipton = 'https://degmods.com/mod/naddr1qqjrgefs89snzdes95urxwfn956rxd3c943r2wtr95mxxdf5v33rswfs8q6nwq3qzm3rm4rd3aqnk65e8l642kzp6xkhdh67g4rk4k5ytyl4a4xpuxcsxpqqqpu6vepxnzn'
$fed = 'https://degmods.com/mod/naddr1qqjrgwfexvcrgve3943rwd3j956xgdnz943ryvnz95mrgwpcvcungef4vsexyq3qrwfel2zca4mw3khvfuclr3jng0qta9r8em94p3eat08jc84aceusxpqqqpmvyhypnhp'
$halo = 'https://degmods.com/mod/naddr1qqjrwwfsxc6nzdrp956rxvrp956rgdm995ukyctp95erxwpex9snjd3kv4skgq3qkgyzsapv0f32r3z5w5yvt52patnpnsnkwja626zxklx84jzgqg9qxpqqqpu6vwksxva'
$cars = 'https://degmods.com/mod/naddr1qqjrqvrzvfjrvd3j95urzv3n956rvdp4943rjef395mn2cnyxsmkxvecxueryq3q3jtme6yr2qz849ms9elnz9cgfv5k2pya05snshshtm2h2ky8jreqxpqqqpmvytwz4hk'
$wifi = 'https://degmods.com/mod/naddr1qqjrzep48qmrse3j95uxvcfn956xyvec943rzve495mrgwp5xd3nqdekvcukyq3qcdcjuqkz9490hfjph29zl8d8ynq7fnm8aktuf9j7ka3dvquz96usxpqqqpu6v760q43'
$blocked = 'https://degmods.com/mod/naddr1qqjrqvmyx4jrwef3956nxvec956rswry943xywrr94jxzv34x9jnvvnrv5erwq3qqqqugpv34nt2fek9yncm8trhkt5rnyqzkwxfwd96vy93mxz6utpqxpqqqpu6vc3j5th'
```

- [ ] **15.1 Games.** `ms mod games vegas --source degmods`, then `ms mod games --source degmods --page-size 100`.
  "Fallout: New Vegas" with the key `fallout: new vegas`; then about 250 games, those DEG Mods' mods are of, read in a few seconds. `account list` has no DEG Mods row: it needs no account.
- [ ] **15.2 A mod's page.** `ms mod show $newspapers`.
  "Better Newspapers for New Vegas Bounties II", "Game: Fallout: New Vegas", by its author's `npub1…`, and its page. Under "Main files", the first file, version 1.0, marked `*`; under "Optional files", the LE version and the alternative texture. Each link is the mod's page with `#file=` and 16 hexadecimal digits.
- [ ] **15.3 A game named alone.** `ms mod search --game "fallout: new vegas" --page-size 5`, then `ms mod search --game starfield --page-size 5`, then `ms mod search --game "degmods/starfield" --page-size 5`.
  The first searches DEG Mods, the newest first, as the game's page on DEG Mods lists them; the second Nexus Mods, a name being Nexus Mods'; the third DEG Mods' Starfield, the music replacers among the newest.
- [ ] **15.4 Words, author, time and order.** `ms mod search newspapers --game "fallout: new vegas"`, `ms mod search --game starfield --source degmods --author npub1kgyzsapv0f32r3z5w5yvt52patnpnsnkwja626zxklx84jzgqg9qj5fkyk`, `ms mod search --game "fallout: new vegas" --updated-within 30 --sort Updated`, then `ms mod search --game "fallout: new vegas" --sort Name --ascending --page-size 5`.
  Better Newspapers; the author's eight music replacers; the mods changed in the last month, the latest first; the first five by name. `--sort Downloads` and `--category Armor` are refused, saying what DEG Mods can.
- [ ] **15.5 Adult content.** `ms mod search --game starfield --source degmods --page-size 50`, then the same with `--adult`.
  The second lists the mods the first left out, those DEG Mods marks NSFW; the first says how many it left out, or lists them all when none is.
- [ ] **15.6 From another file server.** `ms download $newspapers --to $deg`.
  "Checking the file", "Done", and `Better Newspapers for New Vegas Bounties II-96533-1-0-1771837702.zip`, from `blossom.primal.net`, as the log says, its SHA-256 checked. Exit code 0. Nothing asked of `brs.degmods.com`.
- [ ] **15.7 Only on DEG Mods' server.** `ms download $nipton --to $deg`.
  The mod's page opens in the browser, shown with it, and the command waits for a link: DEG Mods' page shows its ad, then downloads the file. Press Ctrl+C: the download waits, exit code 3. The log says only DEG Mods' own server has the file. Cancel it in the desktop app or leave it.
- [ ] **15.8 catbox.moe and the Internet Archive.** `ms download $fed --to $deg`; then `ms mod show $nipton --all`, copy the third file's link, and `ms download "<that link>" --to $deg`.
  `wa4qcn.7z` from catbox.moe; then `NRR1.3.7z` from archive.org, through its redirect. Exit code 0 for each.
- [ ] **15.9 A page.** `ms download $halo --to $deg`.
  MediaFire's page opens in the browser, as in 15.7, after ModShelf asked it what it gives: the log says the file is on a page at `www.mediafire.com`.
- [ ] **15.9a A file elsewhere.** `ms download $cars --to $deg`; once it's under way, Ctrl+C.
  No browser opens: ModShelf downloads `14Y3yg8yWsCn.7z` from `f.rpghq.org` itself, 470 MB, with its progress. Ctrl+C stops it; delete the partial file.
- [ ] **15.10 Into the library, and an update.** `ms download "$wifi#file=89bc8f13f7654384"`, then `ms library check` and `ms library updates`.
  "Added to the library."; the file under `degmods/street fighter 6/Reject Wifi (…)`. The check finds no update: version 4.0 is an optional file, not this one's update.
- [ ] **15.11 Updates.** `ms mod updates --game "fallout: new vegas" --source degmods --since 28d`.
  The mods changed in the last 28 days, in one request to each relay; `--since 60d` is refused, as for Nexus Mods.
- [ ] **15.12 Moderation.** `ms mod show $blocked`.
  "DEG Mods' moderation hides this mod, so ModShelf doesn't download it.", exit code 2. The log says a hidden mod was asked for, without saying which.
- [ ] **15.13 Refusals.** `ms mod show "https://degmods.com/game/Starfield"`, `ms mod show "https://degmods.com/mod/not-an-address"`, then `ms mod show "$newspapers#file=0123456789abcdef"`.
  "This DEG Mods link isn't to a mod. Copy the link of the mod's page."; the same; then "The DEG Mods mod no longer lists that file. Copy the link of the mod's page." Exit code 2 for each.
- [ ] **15.14 A relay down.** With the network cut, `ms mod games --source degmods`.
  It says DEG Mods' relay couldn't be reached, after 30 seconds at most, exit code 1.

## Clean up

After both accounts:

```powershell
Remove-Item -Recurse -Force $test
Remove-Item Env:ModShelf__DatabasePath, Env:ModShelf__SecretsFolder, Env:ModShelf__SecretScope, Env:ModShelf__DesktopApp
```

On macOS and Linux, unlinking already removed the keys from the Keychain or the keyring. If a run stopped before 11.4, run `ms links give-back` before deleting the test folder: the links' registration starts the desktop app on it.

## Results

Copy this table into the release's notes or issue, and mark each check ✅, ❌ or – (not run). For a ❌, add the command, the exit code and what the CLI printed, with any key or `nxm://` link removed.

| Check | Premium | Free | Notes |
|---|---|---|---|
| 1.1 Wrong key refused | | | |
| 1.2 Key links the account | | | |
| 1.3 Account listed | | | |
| 2.1 Mod's page | | | |
| 2.2 Old files and changelog | | | |
| 2.3 File's link | | | |
| 3.1 Search words | | | |
| 3.2 Order and page size | | | |
| 3.3 Author and next page | | | |
| 3.4 No match | | | |
| 3.4a Games by name | | | |
| 3.5 Own order and endorsements | | | |
| 3.6 Category and updated within | | | |
| 4.1 Download a mod's page | | – | |
| 4.2 Download a file's link | | – | |
| 4.3 Queue | | – | |
| 5.1 Page opens | – | | |
| 5.2 Not a link | – | | |
| 5.3 Ctrl+C while waiting | – | | |
| 5.4 Page again | – | | |
| 5.5 Handed-over link | – | | |
| 5.6 The link alone | – | | |
| 5.7 Link to another file | – | | |
| 5.8 The file's page at Link: | – | | |
| 5.9 Prompts with the output in a file | – | | |
| 6.1 Ctrl+C keeps the part | | | |
| 6.2 Continues where it stopped | | | |
| 6.3 Survives a crash | | – | |
| 7.1 Updates, last day | | | |
| 7.2 7 and 28 days | | | |
| 7.3 Too far back | | | |
| 7.4 Unknown game | | | |
| 7.5 A game's own settings | | | |
| 7.5a A game's previews | | | |
| 7.5b A game's own folder | | | |
| 8.1 Tracked list | | – | |
| 8.2 Track | | – | |
| 8.3 Track again | | – | |
| 8.4 Untrack | | – | |
| 8.5 Untrack again | | – | |
| 9.1 Unknown mod | | | |
| 9.2 Collection | | | |
| 9.3 Not a mod's page | | | |
| 9.4 File servers' link | | | |
| 10.1 Trace logs | | | |
| 10.2 Handed-over link in logs | – | | |
| 10.3 Diagnostics report | | | |
| 11.1 Who opens links | | | |
| 11.2 Take them | | | |
| 11.3 A click opens the app | | | |
| 11.4 Give them back | | | |
| 11.5 Unknown site | | | |
| 12.1 Unlink | | | |
| 12.2 Listed as not linked | | | |
| 12.3 Needs an account | | | |
| 12.4 Search without an account | | | |
| 13.1 GameBanana mod's page | | – | |
| 13.2 Old versions and changelog | | – | |
| 13.3 Games by name (time: …) | | – | |
| 13.4 A game's mods, key alone | | – | |
| 13.5 Words, author, category | | – | |
| 13.6 Adult content | | – | |
| 13.7 What it can't search | | – | |
| 13.8 Download by its link | | – | |
| 13.9 Link behind a scheme | | – | |
| 13.10 Into the library | | – | |
| 13.11 Refusals | | – | |
| 13.12 Forget it | | – | |
| 13.13 Link a profile | | – | |
| 13.14 By its link, and refusals | | – | |
| 13.15 Subscribed mods | | – | |
| 13.16 Not changed here | | – | |
| 13.17 Unlink it | | – | |
| 14.1 Nothing without a credential | | – | |
| 14.2 A wrong key | | – | |
| 14.3 The API key links | | – | |
| 14.4 Games | | – | |
| 14.5 A mod's page | | – | |
| 14.6 A game's key alone | | – | |
| 14.7 Words, author, tag, time, order | | – | |
| 14.8 Adult content | | – | |
| 14.9 Download by its link | | – | |
| 14.10 Into the library, its update | | – | |
| 14.11 Subscriptions need a token | | – | |
| 14.12 A token, with subscriptions | | – | |
| 14.13 Not changed here | | – | |
| 14.14 Updates | | – | |
| 14.15 Refusals | | – | |
| 14.16 Nothing secret in the logs | | – | |
| 14.17 Unlink it | | – | |
| 15.1 Games | | – | |
| 15.2 A mod's page | | – | |
| 15.3 A game named alone | | – | |
| 15.4 Words, author, time and order | | – | |
| 15.5 Adult content | | – | |
| 15.6 From another file server | | – | |
| 15.7 Only on DEG Mods' server | | – | |
| 15.8 catbox.moe and the Internet Archive | | – | |
| 15.9 A page | | – | |
| 15.9a A file elsewhere | | – | |
| 15.10 Into the library, and an update | | – | |
| 15.11 Updates | | – | |
| 15.12 Moderation | | – | |
| 15.13 Refusals | | – | |
| 15.14 A relay down | | – | |
