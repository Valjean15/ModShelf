# Manual checklist

CI never calls a live mod site, so before each release someone runs these checks by hand against Nexus Mods: once with a **Premium** account and once with a **free** one. Phase 3 is done when they all pass.

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

Run sections 1 to 12 with the Premium account first. Then run them again with the free account, skipping the checks marked **Premium only**. Each account downloads into a folder of its own, so the second run doesn't find the first one's files:

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

## 8. Tracked mods (Premium only)

This section changes your account. If SMAPI is tracked when you start, track it again at the end.

- [ ] **8.1 The list.** `ms mod tracked`.
  "The account tracks *N* mods on nexus.", by game and then by number. It matches the Tracking Centre on the website.
- [ ] **8.2 Track.** `ms mod track "https://www.nexusmods.com/stardewvalley/mods/2400"`.
  "Tracking mod 2400 of stardewvalley on nexus." Exit code 0. `ms mod tracked` lists `stardewvalley 2400`, and the website shows SMAPI as tracked.
- [ ] **8.3 Track again.** The same command.
  The same message, exit code 0.
- [ ] **8.4 Untrack.** `ms mod untrack "https://www.nexusmods.com/stardewvalley/mods/2400"`.
  "No longer tracking mod 2400 of stardewvalley on nexus." It's gone from `ms mod tracked` and from the website.
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

  The first line is "ModShelf 0.9.0-alpha.0.N (…)", where the part in brackets is the commit you built, as `git rev-parse --short=7 HEAD` shows it. Then the system, the folders with your home folder as `~`, the settings, the library's counts, the downloads by what they're doing, and "nexus linked (ApiKey, Premium)" (Free for the free account). Nowhere does it hold your account's name, a mod's or file's name, a key or an `nxm://` link. Exit code 0.

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
