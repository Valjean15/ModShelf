# Manual checklist: the desktop app

CI never calls a live mod site, and never clicks through the desktop app, so before each release someone runs these checks by hand against Nexus Mods: once with a **Premium** account and once with a **free** one. Phase 5 is done when every flow of Phases 1 to 4 works from the app, without the command line. The command line has its own checklist, [manual-checklist.md](manual-checklist.md).

The checks download files into a test folder, and keep the library database, settings and credentials apart from your own. One section changes your account: it tracks SMAPI and untracks it again, leaving your tracked mods as they were.

Each check says what to do, then what you should see. Quoted text is what the app shows.

## Before you start

You need:

- the personal API keys of a Premium account and a free account, from [your API keys page](https://next.nexusmods.com/settings/api-keys);
- a browser signed in to Nexus Mods with the **free** account, for section 5;
- PowerShell 7 (`pwsh`), from the repository root.

ModShelf doesn't register itself for `nxm://` links until Phase 6, so section 5 copies the link from the website's button. If Vortex or Mod Organizer 2 handles `nxm://` links, that's fine: the button's link is copied, not opened.

Set up a folder of its own for the checks, and build:

```powershell
$test = Join-Path ([System.IO.Path]::GetTempPath()) 'ModShelf-desktop-checklist'
$env:ModShelf__DatabasePath = Join-Path $test 'library.db'
$env:ModShelf__SecretsFolder = Join-Path $test 'Secrets'
$env:ModShelf__SecretScope = 'ModShelf.Checklist'
dotnet build
$app = Resolve-Path src/ModShelf.Desktop/bin/Debug/net10.0/ModShelf.Desktop.dll
function app { Start-Process dotnet -ArgumentList $app }
```

Run sections 1 to 13 with the Premium account first, then again with the free account, skipping the checks marked **Premium only** and running those marked **free only**. Before the second run, close the app (the tray menu's **Quit ModShelf**), and delete the test folder, so the free account starts afresh:

```powershell
Remove-Item -Recurse -Force $test
```

A new terminal needs the setup lines again. Never paste an API key or an `nxm://` link into a report: both carry keys.

## 1. Start

- [ ] **1.1 The window.** `app`.
  The main window opens on the Library screen: "The library is empty. Mods you download into it show here, watched for updates." Its title bar and the taskbar show ModShelf's icon, three books on a shelf.
- [ ] **1.2 The tray icon.** Look in the notification area (on Windows, it may be under the arrow that shows hidden icons).
  ModShelf's icon is there. Hovering over it says "ModShelf". Right-clicking it shows **Open ModShelf**, **Check for updates now** and **Quit ModShelf**.
- [ ] **1.3 One app at a time.** Minimize the window, then run `app` again.
  No second window opens: the first one comes back. The log (section 12) has "ModShelf was launched again, and the launch was handed over to this app".
- [ ] **1.4 The library's folder.** On the Settings screen, **Choose another folder…**, and pick a new, empty folder named `Library` in the test folder (`$test`).
  The folder shown is the one picked, with "The library is in … now." under it, and **Use the default folder** is enabled. Nothing is asked, since the library has no files yet.

## 2. Link the account

- [ ] **2.1 The card.** Open the Accounts screen.
  A card for Nexus Mods: "Nexus Mods needs a linked account." and "No account is linked.", with **Link with an API key**. On Windows and macOS, no warning about a keyring.
- [ ] **2.2 A wrong key is refused.** **Link with an API key**, type `not-a-real-key`, and **Link**.
  The dialog hides what you type. Then, on the card: "Nexus Mods refused the API key. Check it, then try again."
- [ ] **2.3 The API key page.** **Link with an API key**, then **Open the API key page**.
  Your API keys page opens in the browser. **Cancel** closes the dialog, and the card says nothing new.
- [ ] **2.4 The key links the account.** **Link with an API key**, paste the key, and **Link**.
  "Linked as *your name*." The card shows your name, "Premium" (or "Free") in a tag, and "Linked on *today*". Under "API requests left, as Nexus Mods last said:", a line per day and per hour, such as "Per day: 19,999 of 20,000 left, until …". **Check** and **Unlink** appear.
- [ ] **2.5 Check.** **Check**.
  "Checked at *now*." The requests left went down by one or two.

## 3. Browse

- [ ] **3.1 The games.** Open the Browse screen.
  The game box lists Nexus Mods' games. With nothing in the library yet, it starts with none chosen: "Choose a game to search its mods."
- [ ] **3.2 Search.** Choose Stardew Valley (type to filter the list), type `smapi` in the search box, and **Search**.
  "*N* mods match." SMAPI is near the top, with its downloads, endorsements and last update.
- [ ] **3.3 Order and pages.** Choose **Newest** in the order box, then **Load more** at the bottom.
  The list starts over, newest first, and **Load more** adds the next mods below.
- [ ] **3.4 Author.** Type `Pathoschild` in the author box, and search again.
  Only Pathoschild's mods.
- [ ] **3.5 A mod's page.** Clear the author box, search `smapi`, and click SMAPI.
  Its name, author, version, last update, endorsements and downloads, and **Open page**. Its main file is listed with **Download** and **Save to a folder…**. **Show *N* more files: old versions, …** lists the old versions; **Changelog** shows the changelog, or "The mod has no changelog."
- [ ] **3.6 A link in the search box.** **Back to the list**, paste `https://www.nexusmods.com/stardewvalley/mods/2400` into the search box, and **Search**.
  SMAPI's page opens.
- [ ] **3.7 Links it refuses.** Paste each, and **Search**:
  - `https://www.nexusmods.com/stardewvalley/mods/999999999`: "The site has no such mod."
  - `https://example.com/file.zip`: "No site ModShelf knows has mods at that link."

## 4. Download into the library (Premium only)

- [ ] **4.1 From a mod's page.** On SMAPI's page, **Download** on its main file.
  A note says where it downloads, and that it shows on the Downloads screen. The Downloads screen shows it with its progress, speed and time left, then "Done".
- [ ] **4.2 In the library.** Open the Library screen.
  Stardew Valley, then SMAPI, "Watched for updates", "1 file". Expanded: the file's name, version, size and "Downloaded *today*". **Open folder** opens SMAPI's folder, `Library\nexus\stardewvalley\<SMAPI's name> (2400)` in the test folder, holding the file. **Open page** opens the mod's page.
- [ ] **4.3 Again.** On the Downloads screen, paste SMAPI's link (`https://www.nexusmods.com/stardewvalley/mods/2400`) and **Download**.
  "… is in the library already." Nothing new downloads.
- [ ] **4.4 An old version.** On SMAPI's page in Browse, **Show … more files**, then **Download** on an old version.
  It downloads into SMAPI's folder too. The Library shows SMAPI with 2 files.
- [ ] **4.5 Delete a file.** On the Library screen, expand SMAPI, and **Delete…** the old version.
  "Delete this file?" **Delete**: the file is gone from SMAPI's folder, and SMAPI shows 1 file.

## 5. Download through the website (free only)

- [ ] **5.1 It waits for you.** On SMAPI's page in Browse, **Download** on its main file. Open the Downloads screen.
  The download says "Waiting for you: start the download on Nexus Mods, then paste the link it hands over", with **Open on Nexus Mods** and **Paste link…**. The Downloads screen's badge counts 1.
- [ ] **5.2 The page.** **Open on Nexus Mods**.
  The file's page opens on the Files tab.
- [ ] **5.3 Not a link.** **Paste link…**, type `hello`, and **Continue**.
  "That isn't a link. Copy the link the download button on Nexus Mods holds, and paste that."
- [ ] **5.4 The link.** On the website, start the file's download with the download for mod managers (on the "Slow download" page, the button's link). Copy the `nxm://` link it holds (right-click, copy link), then **Paste link…**, paste it, and **Continue**.
  The download starts, and ends "Done". The badge goes. The Library shows SMAPI with its file.
- [ ] **5.5 Another file's link.** Start an old version of SMAPI's download from its page in Browse, then paste the `nxm://` link of a different file into its **Paste link…**.
  "That link is to another file, *…*. Paste the link for *…*."
- [ ] **5.6 The link in the box.** Paste the right file's `nxm://` link into the Downloads screen's own link box, and **Download**.
  The waiting download goes on, and ends "Done".

## 6. Save to a folder

- [ ] **6.1 A folder of your own.** On the Downloads screen, paste SMAPI's link, and **Save to a folder…**. Pick a new folder in the test folder. On the free account, start it on the website and paste the link as in 5.4.
  "Downloading … into …", then "Done". The file is in the folder you picked, and the Library doesn't list it.
- [ ] **6.2 Again.** The same link, **Save to a folder…**, and the same folder.
  "… is saved in … already." Nothing new downloads.

## 7. Pause, resume and cancel

Use a file of a few hundred MB: for example a texture pack's main file. Copy its link from the mod's Files tab.

- [ ] **7.1 Pause.** Paste the link on the Downloads screen and **Download** (on the free account, start it on the website and paste the link). At about a third, **Pause**.
  "Paused". The mod's folder in the library holds a `.part` file.
- [ ] **7.2 Resume.** **Resume** (on the free account, it may wait for a new link: start it again on the website, and paste it).
  It goes on from about a third, not from zero, and ends "Done".
- [ ] **7.3 Quit halfway** (Premium only). Start another large file, and at about half, **Quit ModShelf** from the tray menu. Then `app`.
  The download goes on from where it was, and ends "Done".
- [ ] **7.4 Cancel.** Start another large file, and **Cancel** halfway.
  "Stop this download for good?" **Stop and delete**: "Cancelled", and the `.part` file is gone. **Clear finished** takes the done and cancelled downloads off the list; their files stay.

## 8. Updates

An update needs a mod whose newest file the library doesn't have. Content Patcher's old versions lead to its newest main file.

- [ ] **8.1 An old version.** In Browse, paste `https://www.nexusmods.com/stardewvalley/mods/1915` into the search box and **Search**. **Show … more files**, then **Download** on one of the newest old versions. On the free account, start it on the website and paste the link as in 5.4.
  The Library lists Content Patcher, watched, with that file.
- [ ] **8.2 Check now.** Open the Updates screen, and **Check now**.
  "Checked *N* mods with *N* requests. 1 new update." Content Patcher's newest main file is listed: its version, "Uploaded …", and "Replaces …" the old version. The Updates screen's badge counts 1.
- [ ] **8.3 Changes.** **Changes** on the update.
  Its version's entry in Content Patcher's changelog, or "The changelog says nothing about this version."
- [ ] **8.4 Download the update.** **Download** on the update (on the free account, start it on the website and paste the link).
  "Downloading into …", Content Patcher's folder. Once done, the update leaves the list and the badge goes. The Library's Content Patcher has both files: the default rule keeps the newest 2 versions.

## 9. Watch and track

- [ ] **9.1 Unwatch.** On the Library screen, **Unwatch** SMAPI.
  "Not watched". **Check now** on the Updates screen counts one mod fewer.
- [ ] **9.2 Watch from Browse.** Open a mod you haven't downloaded in Browse, and **Watch**.
  "Watched for updates, in the library." The Library lists it, with no files.
- [ ] **9.3 Track** (Premium only). On SMAPI's page in Browse, **Track on Nexus Mods**.
  "Tracked on Nexus Mods." **Tracked mods** for Stardew Valley lists SMAPI, and the website's Tracking Centre shows it.
- [ ] **9.4 Untrack** (Premium only). **Untrack on Nexus Mods**.
  "No longer tracked on Nexus Mods." It's gone from **Tracked mods** and from the website. If SMAPI was tracked before you started, track it again.

## 10. Settings

- [ ] **10.1 Folder patterns.** Under "Folders in the library", change every game's pattern to `{source}/{game}/{modid}/{file}`.
  The example under it changes as you type. `{mod}/{file}x` says what's wrong, and **Save** is disabled. With the right pattern, **Save**: a mod the library doesn't have yet then downloads to `nexus\<game>\<mod's ID>\…`. Mods in the library keep their folders, and files downloaded already stay where they are.
- [ ] **10.2 A game's own pattern.** **Add a game…**, type `stardewvalley`, then change its pattern and **Save**; then **Remove** it.
  The row's example shows the game. Once removed, the game goes by every game's pattern again.
- [ ] **10.3 Old versions.** Choose **Move the old versions to an _archive folder beside it**, then **Keep the newest versions** with 3.
  Each is kept at once: the settings file (10.7) holds `"oldVersions": 3`. The rule applies when a newer version of a file is downloaded from then on.
- [ ] **10.4 Updates.** Choose **Every hour**, and untick **Show a notice when a check finds new updates**.
  Kept at once: closing and starting the app shows them as chosen.
- [ ] **10.5 Theme.** Choose **Dark**, then **Light**, then **As the system is**.
  The app changes at once each time. Close it (**Quit ModShelf**) with **Dark** chosen and start it again: it starts dark.
- [ ] **10.6 Move the library** (Premium only). **Choose another folder…**, and pick another new folder in the test folder.
  "Move the library?" says how many files move, from where to where. **Move**: "The library is in … now." The files are in the new folder, in the same folders, and gone from the old one. **Open folder** on the Library screen opens the new place.
- [ ] **10.7 Where they're kept.** The bottom of the Settings screen.
  "Settings are kept in …\settings.json." in the test folder. The file holds what you chose.

## 11. The tray and closing the window

- [ ] **11.1 The first close asks.** Close the main window.
  "Keep ModShelf running?" with **Keep running in the tray** and **Quit ModShelf**. Closing this question with its own close button keeps the window open.
- [ ] **11.2 Keep running.** Close the window again, and **Keep running in the tray**.
  The window goes; the tray icon stays. A download started before closing goes on: open the window again to see.
- [ ] **11.3 Back from the tray.** Click the tray icon.
  The window comes back. **Open ModShelf** in the tray menu does the same, and so does `app` from a terminal.
- [ ] **11.4 Remembered.** Close the window.
  It goes without asking. On the Settings screen, "Closing the window" shows **Keep running in the tray**.
- [ ] **11.5 Check from the tray** (with a watched mod). With the window closed, **Check for updates now** in the tray menu.
  A notice from ModShelf says what the check found, such as "Checked 2 mods with 1 request. No new updates."
- [ ] **11.6 The tooltip.** With an update found, as in 8.2 before it's downloaded, hover over the tray icon.
  "ModShelf: 1 update". (On Linux, the tray shows it as the icon's title.)
- [ ] **11.7 Quit closes.** On the Settings screen, choose **Quit ModShelf** under "Closing the window", then close the window.
  ModShelf ends: the tray icon goes. `app` starts it again.
- [ ] **11.8 Quit from the tray.** **Quit ModShelf** in the tray menu.
  ModShelf ends, window and icon.

## 12. Nothing secret in the logs

- [ ] **12.1 The log files.** Close the app, then search its log files:

  ```powershell
  Select-String -Path (Join-Path $test 'logs\desktop-*.log') -Pattern 'key=', 'nxm://', 'apikey'
  ```

  Every line found reads `apikey: *`, if any: the log names the header but never shows its value. No line holds `key=` or `nxm://`.

## 13. Unlink the account

- [ ] **13.1 Unlink.** On the Accounts screen, **Unlink**.
  "Unlink Nexus Mods?" **Unlink**: "Unlinked. Its stored credentials are deleted." The card says "No account is linked." On Windows, `account_nexus.secret` is gone from the test folder's `Secrets` folder.
- [ ] **13.2 Browse needs it.** Open the Browse screen, and **Try again** if it shows a message.
  "Nexus Mods needs your account linked, or refused the one linked. …", with **Go to Accounts**, which opens the Accounts screen.

## Clean up

After both accounts, quit the app from the tray menu, then:

```powershell
Remove-Item -Recurse -Force $test
Remove-Item Env:ModShelf__DatabasePath, Env:ModShelf__SecretsFolder, Env:ModShelf__SecretScope
```

On macOS and Linux, unlinking already removed the keys from the Keychain or the keyring.

## Results

Copy this table into the release's notes or issue, and mark each check ✅, ❌ or – (not run). For a ❌, add what you did and what the app showed, with any key or `nxm://` link removed, and the matching lines of the log.

| Check | Premium | Free | Notes |
|---|---|---|---|
| 1.1 Window | | | |
| 1.2 Tray icon | | | |
| 1.3 One app at a time | | | |
| 1.4 Library's folder | | | |
| 2.1 Card | | | |
| 2.2 Wrong key refused | | | |
| 2.3 API key page | | | |
| 2.4 Key links the account | | | |
| 2.5 Check | | | |
| 3.1 Games | | | |
| 3.2 Search | | | |
| 3.3 Order and pages | | | |
| 3.4 Author | | | |
| 3.5 Mod's page | | | |
| 3.6 Link in the search box | | | |
| 3.7 Links it refuses | | | |
| 4.1 From a mod's page | | – | |
| 4.2 In the library | | – | |
| 4.3 Again | | – | |
| 4.4 Old version | | – | |
| 4.5 Delete a file | | – | |
| 5.1 It waits for you | – | | |
| 5.2 The page | – | | |
| 5.3 Not a link | – | | |
| 5.4 The link | – | | |
| 5.5 Another file's link | – | | |
| 5.6 The link in the box | – | | |
| 6.1 A folder of your own | | | |
| 6.2 Again | | | |
| 7.1 Pause | | | |
| 7.2 Resume | | | |
| 7.3 Quit halfway | | – | |
| 7.4 Cancel | | | |
| 8.1 An old version | | | |
| 8.2 Check now | | | |
| 8.3 Changes | | | |
| 8.4 Download the update | | | |
| 9.1 Unwatch | | | |
| 9.2 Watch from Browse | | | |
| 9.3 Track | | – | |
| 9.4 Untrack | | – | |
| 10.1 Folder patterns | | | |
| 10.2 A game's own pattern | | | |
| 10.3 Old versions | | | |
| 10.4 Updates | | | |
| 10.5 Theme | | | |
| 10.6 Move the library | | – | |
| 10.7 Where they're kept | | | |
| 11.1 First close asks | | | |
| 11.2 Keep running | | | |
| 11.3 Back from the tray | | | |
| 11.4 Remembered | | | |
| 11.5 Check from the tray | | | |
| 11.6 Tooltip | | | |
| 11.7 Quit closes | | | |
| 11.8 Quit from the tray | | | |
| 12.1 Log files | | | |
| 13.1 Unlink | | | |
| 13.2 Browse needs it | | | |
