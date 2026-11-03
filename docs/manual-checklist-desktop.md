# Manual checklist: the desktop app

CI never calls a live mod site, and never clicks through the desktop app, so before each release someone runs these checks by hand against Nexus Mods: once with a **Premium** account and once with a **free** one. Phase 5 is done when every flow of Phases 1 to 4 works from the app, without the command line. The command line has its own checklist, [manual-checklist.md](manual-checklist.md).

The checks download files into a test folder, and keep the library database, settings and credentials apart from your own. One section changes your account: it tracks SMAPI and untracks it again, leaving your tracked mods as they were.

Each check says what to do, then what you should see. Quoted text is what the app shows.

## Before you start

You need:

- the personal API keys of a Premium account and a free account, from [your API keys page](https://next.nexusmods.com/settings/api-keys);
- a browser signed in to Nexus Mods with the **free** account, for section 5;
- PowerShell 7 (`pwsh`), from the repository root.

Section 5 copies the link from the website's button, so it doesn't matter there which app opens `nxm://` links. Section 12 makes ModShelf open them, then gives them back to the app that had them, such as Vortex or Mod Organizer 2.

Set up a folder of its own for the checks, and build:

```powershell
$test = Join-Path ([System.IO.Path]::GetTempPath()) 'ModShelf-desktop-checklist'
$env:ModShelf__DatabasePath = Join-Path $test 'library.db'
$env:ModShelf__SecretsFolder = Join-Path $test 'Secrets'
$env:ModShelf__SecretScope = 'ModShelf.Checklist'
dotnet build
$app = Resolve-Path src/ModShelf.Desktop/bin/Debug/net10.0/ModShelf.Desktop.dll
function app { Start-Process dotnet -ArgumentList "`"$app`"" }
```

The quotes around the path matter: `Start-Process` passes it on as it is, so a path with a space, such as `D:\Repository backups\ModShelf`, would reach `dotnet` as two arguments, and its window would close at once.

Run sections 1 to 14 with the Premium account first, then again with the free account, skipping the checks marked **Premium only** and running those marked **free only**. Before the second run, close the app (the tray menu's **Quit ModShelf**), and delete the test folder, so the free account starts afresh:

```powershell
Remove-Item -Recurse -Force $test
```

A new terminal needs the setup lines again. Never paste an API key or an `nxm://` link into a report: both carry keys.

## 1. Start

- [ ] **1.1 The window and the setup.** `app`.
  The main window opens on the Library screen, "The library is empty. Mods you download into it show here, watched for updates.", and over it "Welcome to ModShelf", "Step 1 of 3", "Where your mods go", with the default folder. Its title bar and the taskbar show ModShelf's icon, three books on a shelf.
- [ ] **1.2 Closing the setup.** Close the setup with its window's close button, **Quit ModShelf** from the tray menu, and `app` again.
  The setup is back, at "Step 1 of 3".
- [ ] **1.3 Its pages.** **Choose another folder…**, and pick a new, empty folder named `Library` in the test folder (`$test`). Then **Next**, and **Next** again, and **Back**.
  The folder shown is the one picked, with "The library is in … now." under it. Step 2, "Link your account", has the Nexus Mods card as on the Accounts screen; don't link the account here, section 2 does. Step 3, "Links from websites", says which app opens `nxm://` links, with **Open them with ModShelf**; don't take them here, section 12 does. **Back** returns to step 2.
- [ ] **1.4 Done.** With the Premium account, **Next**, then **Finish**. With the free account, **Skip setup** instead, on any step.
  The setup closes. The Settings screen shows the `Library` folder from 1.3, with **Use the default folder** enabled. `settings.json` in the test folder holds `"setup": { "done": true }`. **Quit ModShelf** from the tray menu and `app` again: the main window opens without the setup.
- [ ] **1.5 The tray icon.** Look in the notification area (on Windows, it may be under the arrow that shows hidden icons).
  ModShelf's icon is there. Hovering over it says "ModShelf". Right-clicking it shows **Open ModShelf**, **Check for updates now** and **Quit ModShelf**.
- [ ] **1.6 One app at a time.** Minimize the window, then run `app` again.
  No second window opens: the first one comes back. The log (section 13) has "ModShelf was launched again, and the launch was handed over to this app".

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
- [ ] **3.2a The game's rows.** Clear the search box.
  The results give way to three rows: **Trending**, **Recently added** (each card says "Added *when*") and **Recently updated** ("Updated *when*"), each scrolling sideways, Trending's mods the ones the website's Stardew Valley page calls trending. **Show all** beside Recently added lists every mod, newest first, and the order box says **Newest**; clearing the search box again brings the rows back without them loading again. With **Off** under "Previews", the rows' cards have no pictures.
- [ ] **3.3 Order and pages.** Choose **Newest** in the order box, then **Load more** at the bottom.
  The list starts over, newest first, and **Load more** adds the next mods below.
- [ ] **3.3a Size, name and unique downloads.** Choose **Largest**, then **Name, A to Z**, then **Most unique downloads**.
  Largest: each mod says its size, the biggest first (several GB at the top). Name, A to Z: names starting with quotes and digits first, then A. Most unique downloads: SMAPI and Content Patcher near the top.
- [ ] **3.3b Time window, category and page size.** Choose **Updated in the last week**, then **Modding Tools** in the category box, then **50 per page**.
  Each searches again: every mod listed was updated in the last week (open one to see its last update), then only Modding Tools, then 50 mods before **Load more**, which keeps all three. Choosing another game sets the category box back to **All categories**, with that game's categories.
- [ ] **3.4 Author.** Type `Pathoschild` in the author box, and search again.
  Only Pathoschild's mods.
- [ ] **3.5 A mod's page.** Clear the author box, search `smapi`, and click SMAPI.
  Its picture across the top (with **Cards**), its name, then "by Pathoschild", "Modding Tools", its version, endorsements, downloads and last update, and its summary. **Open on Nexus Mods**, **Watch** and **Track on Nexus Mods** (Premium). "Files on Nexus Mods" lists its main file with **Download** and **Save to a folder…**; **Show *N* more files: old versions, …** lists the old versions. Beside them (below them in a narrow window), "Changelog" shows the latest three versions, newest first, with **All *N* entries** for the rest, and "Details" says "Mod ID 2400" and "Category Modding Tools".
- [ ] **3.6 A link in the search box.** **Back to the list**, paste `https://www.nexusmods.com/stardewvalley/mods/2400` into the search box, and **Search**.
  SMAPI's page opens.
- [ ] **3.7 Links it refuses.** Paste each, and **Search**:
  - `https://www.nexusmods.com/stardewvalley/mods/999999999`: "The site has no such mod."
  - `https://example.com/file.zip`: "No site ModShelf knows has mods at that link."
- [ ] **3.8 Previews.** **Back to the list**, search `smapi` again, and look at the results with each of **Cards**, **Thumbnails** and **Off** under "Previews".
  **Cards**, the default: each mod is a card with its picture across the top, as many side by side as fit; clicking one opens it. **Thumbnails**: the list, with each mod's thumbnail beside it. **Off**: the list without pictures. A mod without a picture shows a faint picture sign. The choice stays after closing and starting the app.
- [ ] **3.9 Adult pictures.** Tick **Include adult content**, choose Skyrim Special Edition, and search `outfit`.
  The adult mods' pictures are blurred, and their cards say "Adult · blurred". No sharp adult picture shows, even for a moment.
- [ ] **3.10 Select mods.** Choose Stardew Valley, search `smapi`, and tick the boxes of two mods (at the top left of each card; with **Off**, beside each name).
  "2 selected", with **Download selected (2)** and **Clear selection**, and **Select all shown** is half ticked. Clicking a box doesn't open its mod. Ticking **Select all shown** selects every mod listed; **Load more** keeps them, and **Select all shown** is half ticked again. **Clear selection** clears them all, and a new search starts with none selected. Don't download them here: 9.7 does.

## 4. Download into the library (Premium only)

- [ ] **4.1 From a mod's page.** On SMAPI's page, **Download** on its main file.
  A note says where it downloads, and that it shows on the Downloads screen. The Downloads screen shows it with its progress, speed and time left, then "Done".
- [ ] **4.2 In the library.** Open the Library screen.
  "1 game · 1 mod · *size* on disk" under the heading. Stardew Valley's row: a cover with "S", "1 mod", "Up to date", the file's size, and in "Checked" when it was last checked for updates, or "Not yet". Click it: "Library › Stardew Valley", its cover and "1 mod · Up to date · *size* on disk" at the top, then SMAPI's row: its name, "by Pathoschild", your version (the file's), no status, "1 file", the size, and when Nexus Mods last updated it. In its **…** menu, **Open folder** opens SMAPI's folder, `Library\nexus\stardewvalley\<SMAPI's name> (2400)` in the test folder, holding the file. **Open on its site** opens the mod's page in the browser.
- [ ] **4.3 Again.** On the Downloads screen, paste SMAPI's link (`https://www.nexusmods.com/stardewvalley/mods/2400`) and **Download**.
  "… is in the library already." Nothing new downloads.
- [ ] **4.4 An old version.** On SMAPI's page in Browse, **Show … more files**, then **Download** on an old version.
  It downloads into SMAPI's folder too. The Library shows SMAPI with 2 files.
- [ ] **4.5 Delete a file.** Open SMAPI's page from the Library screen. In "Your files", open the old version's **…** menu, and **Delete…**.
  "Delete this file?", with its name and size. **Cancel** leaves it. **Delete…** again, then **Delete**: the file is gone from SMAPI's folder and from "Your files", and back in Stardew Valley's mods, SMAPI shows "1 file".
- [ ] **4.6 Pictures in the library.** On the Library screen, look at the games, then Stardew Valley's mods, with **Thumbnails**, the default, then **Cards**.
  The games: a small cover in each row; then cards with large covers, each with its name, mods, size and updates. The mods: a thumbnail of SMAPI's picture beside its name; then a card with the picture, its name, its **…** menu, "by Pathoschild", its version, "1 file" and size. The test folder now has a `pictures` folder, beside `library.db`, with files named by long hexadecimal names. With the network off, closing and starting the app shows the same pictures.
- [ ] **4.7 A library mod's page.** Click SMAPI's name on the Library screen (or **Open** in its **…** menu).
  The page opens in place of the list, under "Library › Stardew Valley › SMAPI", with the previews at the end of that line. "Your files": SMAPI's folder, then the file with "Main", its name, size, "Downloaded *today*" and its SHA-256 shortened to its ends; the copy button beside it puts the whole SHA-256 on the clipboard. **Open folder** opens SMAPI's folder. In "Files on Nexus Mods", the downloaded file says "In the library" instead of **Download**. "Details" adds "In the library since *today*" and "Last checked". **Rescan** and **Remove from library…** are under the name. **Stardew Valley** in the line at the top goes back to its mods, and **Library** to the games.
- [ ] **4.8 A file missing from disk.** Rename SMAPI's file in its folder, then open SMAPI's page again.
  The file says "Missing from disk". Name it back, **Rescan** on the page: "Rescan finished", and the file no longer says so.
- [ ] **4.9 Offline.** Turn the network off, open the Library screen's SMAPI again.
  A strip says "Nexus Mods couldn't give the mod, so its saved details show: …", with **Try again**. The page shows SMAPI's saved name and picture, and your files; "Files on Nexus Mods" and "Changelog" say "Shown once Nexus Mods gives the mod." Turn the network on and **Try again**: the strip goes, and the whole page shows.

## 5. Download through the website (free only)

- [ ] **5.1 It waits for you.** On SMAPI's page in Browse, **Download** on its main file. Open the Downloads screen.
  The download says "Waiting for you: start the download on Nexus Mods, then paste the link it hands over", with **Open on Nexus Mods** and **Paste link…**. Above the list, "1 file waits for you on Nexus Mods. …" with **Open the next page**; the line under the heading says "1 waiting for you", and the "Waiting for you" tab counts 1. The Downloads screen's badge counts 1.
- [ ] **5.2 The page.** **Open on Nexus Mods**, then **Open the next page**.
  Each opens the file's page on the Files tab.
- [ ] **5.3 Not a link.** **Paste link…**, type `hello`, and **Continue**.
  "That isn't a link. Copy the link the download button on Nexus Mods holds, and paste that."
- [ ] **5.4 The link.** On the website, start the file's download with the download for mod managers (on the "Slow download" page, the button's link). Copy the `nxm://` link it holds (right-click, copy link), then **Paste link…**, paste it, and **Continue**.
  The download starts, and ends "Done". The badge goes. The Library shows SMAPI with its file.
- [ ] **5.5 Another file's link.** Start an old version of SMAPI's download from its page in Browse, then paste the `nxm://` link of a different file into its **Paste link…**.
  "That link is to another file, *…*. Paste the link for *…*."
- [ ] **5.6 The link in the box.** Paste the right file's `nxm://` link into the Downloads screen's own link box, and **Download**.
  The waiting download goes on, and ends "Done".
- [ ] **5.7 The file's page isn't the link handed over.** Start another old version of SMAPI's download from its page in Browse. On the Downloads screen, **Open on Nexus Mods**, copy the page's address from the browser's address bar, then **Paste link…**, paste it, and **Continue**.
  "That's a link to *the file's name*, but not the one Nexus Mods hands over. Start the download on its page, then paste the link its download button holds." **Cancel** the download.

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
- [ ] **7.5 The summary and tabs.** Start a large file and **Pause** it; start another and let it run.
  The line under the heading says "1 downloading at *speed*, 1 paused", then "1 paused" once the second is done. "Under way" counts and shows the paused one, "Finished" the done ones, and "Failed" says "No downloads here." when none failed. **All** shows every download again. Cancel the paused one.

## 8. Updates

An update needs a mod whose newest file the library doesn't have. Content Patcher's old versions lead to its newest main file.

- [ ] **8.1 An old version.** In Browse, paste `https://www.nexusmods.com/stardewvalley/mods/1915` into the search box and **Search**. **Show … more files**, then **Download** on one of the newest old versions. On the free account, start it on the website and paste the link as in 5.4.
  The Library lists Content Patcher, watched, with that file.
- [ ] **8.2 Check now.** Open the Updates screen, and **Check now**.
  "Checked *N* mods with *N* requests. 1 new update." The line under the heading says "1 update for 1 mod · checked just now, next in 6 hours" (the default interval). Under "Stardew Valley", "1 update" and **Update all (1)**: Content Patcher, its change of version in a blue tag ("*old* → *new*"), its file's name, "Uploaded …", and "Replaces …" the old version. The Updates screen's badge counts 1.
- [ ] **8.2a The update on the mod's page.** Open Content Patcher's page from the Library screen.
  In "Files on Nexus Mods", its newest main file says "Update for your *version*" in a tag, and its **Download** is the blue one.
- [ ] **8.2b What it needs.** "Requirements" on Content Patcher's page.
  "SMAPI - Stardew Modding API", "In library" (from 4.1), and **Open**, which opens SMAPI's page in its place, from the top. Under the list: installing them is up to your mod manager. SMAPI's own page has no "Requirements".
- [ ] **8.2c A game's mods.** On the Library screen, open Stardew Valley.
  Content Patcher says "Update to *version*" in a blue tag, and the heading counts "1 update", with **Update all (1)** beside **Rescan**. "Has an update 1" shows Content Patcher alone; with "Watched" on too, still it; both off, both mods. Rename SMAPI's file in its folder, then go back to the games and open Stardew Valley again: "Missing files 1", and SMAPI says "1 file missing" in an orange tag (name the file back, and open the game again). "pathos" in the filter finds Pathoschild's mods by author. "Sort" by **Updates first**, **Updated on site**, **Size** and **Added to library** reorders them, and **Name** puts them back. With the window narrow, each mod's version, files and size go under its author, and the "Your version", "Files", "Size" and "Updated on site" columns go.
- [ ] **8.2d Select mods.** In Stardew Valley's mods, tick SMAPI's box, then Content Patcher's.
  "1 selected" with **Update selected (0)** greyed out, then "2 selected" with **Update selected (1)**, and **Select all shown** goes from half ticked to ticked. "content" in the filter hides SMAPI and unselects it: "1 selected". Clear the filter; **Select all shown** selects both, and **Clear selection** neither. With **Cards**, each card has its box at the top left, and clicking it doesn't open the mod. Don't update them here: 8.4 does.
- [ ] **8.3 Changes.** Look under the update, without clicking anything.
  "What changed in *version*" and its entry in Content Patcher's changelog, or "The changelog says nothing about this version." Going to another screen and back doesn't read it again (the log has no new request).
- [ ] **8.4 Download the update.** **Update all (1)** at the top of the Updates screen.
  "Downloading into …", Content Patcher's folder, under the update. On the free account, the Downloads screen says "1 file waits for you on Nexus Mods. …": **Open the next page** opens the file's page; start it there and paste the link as in 5.4. Once done, the update leaves the list and the badge goes. The Library's Content Patcher has both files: the default rule keeps the newest 2 versions.

## 9. Watch and track

- [ ] **9.1 Unwatch.** On the Library screen, in Stardew Valley's mods, open SMAPI's **…** menu, and **Unwatch**.
  SMAPI says "Not watched"; "Watched" counts one fewer. **Check now** on the Updates screen counts one mod fewer.
- [ ] **9.2 Watch from Browse.** Open a mod of another game you haven't downloaded in Browse, such as SkyUI for Skyrim Special Edition, and **Watch**.
  "Watched for updates, in the library." The Library lists its game, and among the game's mods, it says "Watched only" and "No files".
- [ ] **9.2a The games.** On the Library screen, with the games shown.
  The heading counts 2 games. "Sort" by **Most mods** (the default), **Name**, **Most space** and **Updates first** reorders them; games that tie go by name. "stardew" in the filter leaves Stardew Valley; "zzz" says "No game's name has “zzz”." With the window narrow, each game's mods and updates go under its name, "Checked" goes, and the sort and previews go below the filter.
- [ ] **9.3 Track** (Premium only). On SMAPI's page in Browse, **Track on Nexus Mods**.
  "Tracked on Nexus Mods." **Tracked mods** for Stardew Valley lists SMAPI, and the website's Tracking Centre shows it.
- [ ] **9.4 Untrack** (Premium only). **Untrack on Nexus Mods**.
  "No longer tracked on Nexus Mods." It's gone from **Tracked mods** and from the website. If SMAPI was tracked before you started, track it again.
- [ ] **9.5 Remove from the library.** On the Library screen, open the **…** menu of the mod you watched in 9.2, and **Remove from library…**.
  "Remove this mod from the library?", saying its files stay. **Remove**: the mod leaves the Library, and so does its game, which had no other mod; **Check now** on the Updates screen counts one mod fewer. Removing a mod with files leaves them in its folder.
- [ ] **9.6 Rescan.** Copy a file of SMAPI's from its page on Nexus Mods (**Manual download**) into SMAPI's folder, then **Rescan** at the top of the Library screen.
  "Rescan finished", saying a file found on disk was added; SMAPI shows one file more, with its SHA-256. **Rescan** in SMAPI's **…** menu right after says "Nothing new".
- [ ] **9.7 Download selected.** In Browse, choose Skyrim Special Edition, search `skyui`, select the first two mods, and **Download selected (2)**.
  "Queued 2 files. They show on the Downloads screen.", and the boxes clear; a mod without a main file says instead that it couldn't be queued, and why. The Downloads screen downloads each one's newest main file into its folder in the library (on the free account, both wait for you: start them as in 5.4, or cancel them). Once they're done, select the same two again and **Download selected (2)**: "Their files are in the library already."

## 10. Settings

- [ ] **10.0 Categories and search.** Open the Settings screen, then each category in its list.
  It opens on **General**: the theme, closing the window, and pictures and mod details. **Library**: the library folder, the folder pattern, old versions and **Rescan the library**. **Updates**, **Links**, **Games** (the games' own folder patterns) and **About** show theirs. Type `folder` in **Search settings**: the library folder, the folder pattern, old versions (the _archive folder), the rescan, the games' patterns and About (the log folder), under "Settings that match “folder”", and General, Updates and Links fade in the list. `zzz`: "No setting matches “zzz”." Escape, or choosing a category, ends the search. **Rescan the library** says what it found, as on the Library screen. Make the window narrow: the categories move above the settings, side by side.
- [ ] **10.1 Folder patterns.** In **Library**, under "Folder pattern", change every game's pattern to `{source}/{game}/{modid}/{file}`.
  The example under it changes as you type. `{mod}/{file}x` says what's wrong, and **Save** is disabled. With the right pattern, **Save**: a mod the library doesn't have yet then downloads to `nexus\<game>\<mod's ID>\…`. Mods in the library keep their folders, and files downloaded already stay where they are.
- [ ] **10.2 A game's own pattern.** In **Games**, **Add a game…**, type `stardewvalley`, then change its pattern and **Save**; then **Remove** it.
  The row's example shows the game. Once removed, the game goes by every game's pattern again.
- [ ] **10.3 Old versions.** Choose **Move the old versions to an _archive folder beside it**, then **Keep the newest versions** with 3.
  Each is kept at once: the settings file (10.7) holds `"oldVersions": 3`. The rule applies when a newer version of a file is downloaded from then on.
- [ ] **10.4 Updates.** Choose **Every hour**, and untick **Show a notice when a check finds new updates**.
  Kept at once: closing and starting the app shows them as chosen.
- [ ] **10.5 Theme.** Choose **Dark**, then **Light**, then **As the system is**.
  The app changes at once each time. Close it (**Quit ModShelf**) with **Dark** chosen and start it again: it starts dark.
- [ ] **10.6 Move the library** (Premium only). **Choose another folder…**, and pick another new folder in the test folder.
  "Move the library?" says how many files move, from where to where. **Move**: "The library is in … now." The files are in the new folder, in the same folders, and gone from the old one. **Open folder** in a mod's **…** menu on the Library screen opens the new place.
- [ ] **10.6a Pictures and mod details.** The "Pictures and mod details" card.
  "Saved pictures": "*size* of 512 MB. The oldest go first when it's full.", with a bar. **Clear** empties the test folder's `pictures` folder, and the size says 0 B; the screens download the pictures they show again. Choose **1 GB**, **1 week** under "Mod details" and **Hidden** under "Adult content": the settings file holds `"pictures": { "maxSize": 1024, "adult": "hidden" }` and `"details": { "maxAge": "7d" }`. On Browse (3.9), the adult mods show a struck-through eye instead of a picture. **Shown** shows their pictures as they are. Choose **Blurred (the default)** again.
- [ ] **10.7 Where they're kept.** The bottom of **About**.
  "Settings are kept in …\settings.json." in the test folder. The file holds what you chose.
- [ ] **10.8 The version.** "About ModShelf", in **About**.
  "ModShelf 0.9.0-alpha.0.N (…)", where the part in brackets is the commit you built, as `git rev-parse --short=7 HEAD` shows it. Once a version is tagged, such as `v0.9.0`, a build of that commit shows "ModShelf 0.9.0 (…)".
- [ ] **10.9 Copy diagnostics.** **Copy diagnostics**, then paste into a text editor.
  "The diagnostics are on the clipboard. Paste them into your bug report." The report's first line is the version from 10.8. Then the system, the folders with your home folder as `~`, the settings from 10.1 to 10.5, the library's counts, the downloads by what they're doing, and "nexus linked (ApiKey, Premium)" (Free for the free account). Nowhere does it hold your account's name, a mod's or file's name, a key or an `nxm://` link.
- [ ] **10.10 Open log folder.** **Open log folder**.
  The system's file manager opens the test folder's `logs` folder, with today's `desktop-….log` in it.
- [ ] **10.11 ModShelf's own updates.** Under the version in "About ModShelf".
  "Updates come with the installed ModShelf. This one runs from where it was built, so it doesn't update itself." No **Check for updates** button, and no "Look for a newer ModShelf when it starts" box. The installed app's updates are checked by [the installer checklist](manual-checklist-installer.md).

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
  A notice from ModShelf says what the check found, such as "Checked 2 mods with 1 request. No new updates." On Windows, click the notice: the window comes back on the Updates screen, and `(Get-ItemProperty 'HKCU:\Software\Classes\modshelf\shell\open\command').'(default)'` shows the desktop app's path, `--ModShelf:DatabasePath=` with the test folder's database, and `"%1"`.
- [ ] **11.6 The tooltip.** With an update found, as in 8.2 before it's downloaded, hover over the tray icon.
  "ModShelf: 1 update". (On Linux, the tray shows it as the icon's title.)
- [ ] **11.7 Quit closes.** On the Settings screen, choose **Quit ModShelf** under "Closing the window", then close the window.
  ModShelf ends: the tray icon goes. `app` starts it again.
- [ ] **11.8 Quit from the tray.** **Quit ModShelf** in the tray menu.
  ModShelf ends, window and icon.

## 12. Links from websites

Nexus Mods' **Mod manager download** buttons hand a download over with an `nxm://` link, which the system opens with the app registered for them. These checks make ModShelf that app, then give the links back. Note which app opens them before you start: 12.7 gives them back to it. On macOS, the card says ModShelf can open them once it's installed: skip to section 13, since the installer's checklist checks the rest.

- [ ] **12.1 Who opens them.** On the Settings screen, "Links from websites".
  "Vortex opens nxm:// links from Nexus Mods." (or the app that opens them, or "No app opens …"), with **Open them with ModShelf**.
- [ ] **12.2 Take them.** **Open them with ModShelf**.
  "ModShelf opens nxm:// links from Nexus Mods.", and the button reads **Give them back to Vortex** (or **Stop opening them** when no app had them). On Windows, `(Get-ItemProperty 'HKCU:\Software\Classes\nxm\shell\open\command').'(default)'` shows the desktop app's path, `--ModShelf:DatabasePath=` with the test folder's database, and `"%1"`. On Linux, `xdg-mime query default x-scheme-handler/nxm` says `modshelf-nxm.desktop`.
  If instead the card says another app still opens them, "chosen in the system's own settings": on Windows, choose ModShelf for NXM under Settings > Apps > Default apps > Choose defaults by link type, then open the Settings screen again.
- [ ] **12.3 A link while it runs.** Go to the Library screen. On SMAPI's Files tab on the website, click **Mod manager download** on an older file (with the free account, then **Slow download** on the page it opens). Let the browser open ModShelf.
  ModShelf comes forward on the Downloads screen: "Downloading … into …", then "Done". The Library lists the file.
- [ ] **12.4 A waiting download** (free only). In Browse, **Download** another older SMAPI file: it waits, as in 5.1. On the website, start that file's download for mod managers.
  The waiting download goes on without pasting anything, and ends "Done".
- [ ] **12.5 From the tray.** Close the window to the tray (as in 11.2), then click **Mod manager download** on the website for the file of 12.3 again.
  The window comes back on the Downloads screen: "… is in the library already."
- [ ] **12.6 With the app closed.** **Quit ModShelf** from the tray menu, then click **Mod manager download** on SMAPI's main file.
  ModShelf starts on the Downloads screen, with the test folder's library: the Library screen lists the files from earlier checks. The download ends "Done".
- [ ] **12.7 Give them back.** On the Settings screen, **Give them back to Vortex** (or **Stop opening them**).
  The card says what it did in 12.1. On Windows, the command from 12.2 shows the app's own command again (or the key is gone), and `Test-Path HKCU:\Software\ModShelf` says False. On Linux, `xdg-mime query default x-scheme-handler/nxm` names what it did in 12.1, and `~/.local/share/applications/modshelf-nxm.desktop` is gone.
- [ ] **12.8 Clicked after.** Click **Mod manager download** on the website again.
  The app from 12.1 gets the link, or the browser says nothing opens it. ModShelf doesn't.

## 13. Nothing secret in the logs

- [ ] **13.1 The log files.** Close the app, then search its log files:

  ```powershell
  Select-String -Path (Join-Path $test 'logs\desktop-*.log') -Pattern 'key=', 'nxm://', 'apikey'
  ```

  Every line found reads `apikey: *`, if any: the log names the header but never shows its value. No line holds `key=` or `nxm://`.

## 14. Unlink the account

- [ ] **14.1 Unlink.** On the Accounts screen, **Unlink**.
  "Unlink Nexus Mods?" **Unlink**: "Unlinked. Its stored credentials are deleted." The card says "No account is linked." On Windows, `account_nexus.secret` is gone from the test folder's `Secrets` folder.
- [ ] **14.2 Browse needs it.** Open the Browse screen, and **Try again** if it shows a message.
  "Nexus Mods needs your account linked, or refused the one linked. …", with **Go to Accounts**, which opens the Accounts screen.

## Clean up

If a run stopped before 12.7, give the links back on the Settings screen first: their registration starts ModShelf on the test folder. After both accounts, quit the app from the tray menu, then:

```powershell
Remove-Item -Recurse -Force $test
Remove-Item Env:ModShelf__DatabasePath, Env:ModShelf__SecretsFolder, Env:ModShelf__SecretScope
```

On Windows, the notices registered `modshelf://` links for this test folder's app (11.5). Remove them; an installed ModShelf registers them again before its next notice:

```powershell
Remove-Item -Recurse HKCU:\Software\Classes\modshelf -ErrorAction SilentlyContinue
```

On macOS and Linux, unlinking already removed the keys from the Keychain or the keyring.

## Results

Copy this table into the release's notes or issue, and mark each check ✅, ❌ or – (not run). For a ❌, add what you did and what the app showed, with any key or `nxm://` link removed, and the matching lines of the log.

| Check | Premium | Free | Notes |
|---|---|---|---|
| 1.1 Window and setup | | | |
| 1.2 Closing the setup | | | |
| 1.3 Its pages | | | |
| 1.4 Done | | | |
| 1.5 Tray icon | | | |
| 1.6 One app at a time | | | |
| 2.1 Card | | | |
| 2.2 Wrong key refused | | | |
| 2.3 API key page | | | |
| 2.4 Key links the account | | | |
| 2.5 Check | | | |
| 3.1 Games | | | |
| 3.2 Search | | | |
| 3.2a The game's rows | | | |
| 3.3 Order and pages | | | |
| 3.3a Size, name and unique downloads | | | |
| 3.3b Time window, category and page size | | | |
| 3.4 Author | | | |
| 3.5 Mod's page | | | |
| 3.6 Link in the search box | | | |
| 3.7 Links it refuses | | | |
| 3.8 Previews | | | |
| 3.9 Adult pictures | | | |
| 3.10 Select mods | | | |
| 4.1 From a mod's page | | – | |
| 4.2 In the library | | – | |
| 4.3 Again | | – | |
| 4.4 Old version | | – | |
| 4.5 Delete a file | | – | |
| 4.6 Pictures in the library | | – | |
| 4.7 A library mod's page | | – | |
| 4.8 A file missing from disk | | – | |
| 4.9 Offline | | – | |
| 5.1 It waits for you | – | | |
| 5.2 The page | – | | |
| 5.3 Not a link | – | | |
| 5.4 The link | – | | |
| 5.5 Another file's link | – | | |
| 5.6 The link in the box | – | | |
| 5.7 The file's page | – | | |
| 6.1 A folder of your own | | | |
| 6.2 Again | | | |
| 7.1 Pause | | | |
| 7.2 Resume | | | |
| 7.3 Quit halfway | | – | |
| 7.4 Cancel | | | |
| 7.5 The summary and tabs | | | |
| 8.1 An old version | | | |
| 8.2 Check now | | | |
| 8.2a The update on the mod's page | | | |
| 8.2b What it needs | | | |
| 8.2c A game's mods | | | |
| 8.2d Select mods | | | |
| 8.3 Changes | | | |
| 8.4 Download the update | | | |
| 9.1 Unwatch | | | |
| 9.2 Watch from Browse | | | |
| 9.2a The games | | | |
| 9.3 Track | | – | |
| 9.4 Untrack | | – | |
| 9.5 Remove from the library | | | |
| 9.6 Rescan | | | |
| 9.7 Download selected | | | |
| 10.0 Categories and search | | | |
| 10.1 Folder patterns | | | |
| 10.2 A game's own pattern | | | |
| 10.3 Old versions | | | |
| 10.4 Updates | | | |
| 10.5 Theme | | | |
| 10.6 Move the library | | – | |
| 10.6a Pictures and mod details | | | |
| 10.7 Where they're kept | | | |
| 10.8 Version | | | |
| 10.9 Copy diagnostics | | | |
| 10.10 Open log folder | | | |
| 10.11 ModShelf's own updates | | | |
| 11.1 First close asks | | | |
| 11.2 Keep running | | | |
| 11.3 Back from the tray | | | |
| 11.4 Remembered | | | |
| 11.5 Check from the tray | | | |
| 11.6 Tooltip | | | |
| 11.7 Quit closes | | | |
| 11.8 Quit from the tray | | | |
| 12.1 Who opens them | | | |
| 12.2 Take them | | | |
| 12.3 A link while it runs | | | |
| 12.4 A waiting download | – | | |
| 12.5 From the tray | | | |
| 12.6 With the app closed | | | |
| 12.7 Give them back | | | |
| 12.8 Clicked after | | | |
| 13.1 Log files | | | |
| 14.1 Unlink | | | |
| 14.2 Browse needs it | | | |
