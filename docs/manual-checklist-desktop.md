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
- [ ] **1.7 The window comes back as it was.** Move the window and make it smaller, collapse the sidebar with the button at its top, open Downloads, and **Quit ModShelf** from the tray menu; run `app`. Then maximize it, quit, and run `app` again, and restore it. With two monitors, leave it on the second one, quit, unplug that monitor (or set Windows to show only the first), and run `app`.
  It opens where and as large as it was, the sidebar collapsed, on Downloads. Maximized, it opens maximized, and restoring it gives the size before. With its monitor gone, it opens whole and centred on the main one, no larger than that screen. `ui-state.json` in the test folder has the window's place, `"menuCollapsed"` and `"screen"`.

## 2. Link the account

- [ ] **2.1 The card.** Open the Accounts screen.
  A card for Nexus Mods: "Nexus Mods needs a linked account." and "No account is linked.", with **Link with an API key**. On Windows and macOS, no warning about a keyring. At the bottom of the sidebar, **Link an account**, "To download mods", which opens Accounts from any screen.
- [ ] **2.2 A wrong key is refused.** **Link with an API key**, type `not-a-real-key`, and **Link**.
  The dialog hides what you type. Then, on the card: "Nexus Mods refused the API key. Check it, then try again."
- [ ] **2.3 The API key page.** **Link with an API key**, then **Open the API key page**.
  Your API keys page opens in the browser. **Cancel** closes the dialog, and the card says nothing new.
- [ ] **2.4 The key links the account.** **Link with an API key**, paste the key, and **Link**.
  "Linked as *your name*." The card shows your picture from Nexus Mods (your initial on a colour until it's read), your name, "Premium" (or "Free") in a tag, and "Linked on *today*". The bottom of the sidebar shows the same picture, your name and "Premium · Nexus Mods" (or "Free · Nexus Mods") in place of **Link an account**; clicking it from another screen opens Accounts. With the sidebar's names hidden (the button at its top), the picture shows alone, its tooltip naming you, your level and Nexus Mods. With the network off, closing and starting the app shows the picture saved. Under "API requests left, as Nexus Mods last said:", a line per day and per hour, such as "Per day: 19,999 of 20,000 left, until …". **Check** and **Unlink** appear.
- [ ] **2.5 Check.** **Check**.
  "Checked at *now*." The requests left went down by one or two.
- [ ] **2.5a Requests running low.** In Settings › Updates, set "Warn about a site's requests for the hour or the day" to **When below 30% are left**, then use the app until fewer than 30% of the hour's requests are left (or set it to a share above what Accounts shows left), and wait half a minute. Click the strip's ×. Then, on the Updates screen with fewer requests left than the watched mods, **Check now**. Set the warning back to **When below 10% are left (the default)**.
  A strip at the top of the window says "Nexus Mods has *N* of its *M* requests left this hour. They renew at *time*." (or "today", for the day's); × hides it until they renew. **Check now** asks first, "Requests may run out", saying about how many requests the check may take, how many are left and when they renew; **Cancel** checks nothing, **Go on** checks. `settings show` in a terminal says `quota.warnBelow` 30, then 10 (default).
- [ ] **2.6 Linked from the command line.** With the app open on another screen, run `dotnet run --project src/ModShelf.Cli -- account unlink nexus`, then open Accounts; link it again from the command line (`account link nexus`), and open Accounts again. Link it in the app afterwards if you unlinked it there.
  Opening Accounts shows the account gone, and the sidebar **Link an account**; then linked again, in both, without restarting the app.

## 3. Browse

- [ ] **3.1 The games.** Open the Browse screen.
  The game picker's field says "Find a game"; clicking it opens a list under it saying "Type to find any of the *N* games on Nexus Mods." and which keys do what. With nothing in the library yet, it starts with none chosen: "Choose a game to search its mods."
- [ ] **3.1a Games by name.** Type each in the game picker: `stalker 2`, `final fantasy 12`, `dragons dogma`, `valley`, `dew`. Then type `star`, press ↓ twice and Enter; open it again, type `sky`, and press Escape.
  S.T.A.L.K.E.R. 2: Heart of Chornobyl; Final Fantasy XII: The Zodiac Age; Dragon's Dogma 2; for `valley`, the games whose names start with "Valley" first, then Stardew Valley and the others; for `dew`, "No game's name has “dew”." Clearing the field shows the hint again. ↓ moves the highlight, and Enter chooses the third game: it shows as a chip in the field, and its rows load. Escape closes the list, forgetting `sky`, the chip still there.
- [ ] **3.2 Search.** Choose Stardew Valley (type to filter the list), type `smapi` in the search box, and **Search**.
  "*N* mods match." SMAPI is near the top, with its downloads, endorsements and last update.
- [ ] **3.2a The game's rows.** Clear the search box.
  The results give way to three rows: **Trending**, **Recently added** (each card says "Added *when*") and **Recently updated** ("Updated *when*"), each scrolling sideways, Trending's mods the ones the website's Stardew Valley page calls trending. **Show all** beside Recently added lists every mod, newest first, and the results say "Order: Newest"; clearing the search box again brings the rows back without them loading again. With **Rows** in the view switch beside **Filters** (its tooltips name the views), the rows' cards have no pictures.
- [ ] **3.3 Order and pages.** **Filters**, choose **Newest** in the order box, and **Apply**. Then **Load more** at the bottom.
  The card opens over the results on the right, and the keyboard is in it. Choosing in it searches nothing; **Apply** closes it, and the list starts over, newest first, saying "Order: Newest". **Load more** adds the next mods below.
- [ ] **3.3a Size, name and unique downloads.** In **Filters**, apply **Largest**, then **Name, A to Z**, then **Most unique downloads**.
  Largest: each mod says its size, the biggest first (several GB at the top). Name, A to Z: names starting with quotes and digits first, then A. Most unique downloads: SMAPI and Content Patcher near the top.
- [ ] **3.3a2 Endorsements.** In **Filters**, open the order box, then apply **Most endorsements**, then **Fewest endorsements**.
  It lists 14 orders. Most endorsements: SMAPI and Content Patcher near the top, each mod saying its endorsements. Fewest endorsements: mods with few or none first.
- [ ] **3.3b Time window, category and page size.** In **Filters**, choose **Week** under "Updated within", **Modding Tools** in the category box and **50** under "Per page", then **Apply**.
  One search: every mod listed was updated in the last week (open one to see its last update), only Modding Tools, 50 mods before **Load more**, which keeps all three. Under the search, chips say "Updated in the last week" and "Category: Modding Tools", and the button says "Filters · 2"; the page size has no chip.
- [ ] **3.3c Chips, Close and Reset.** Click the × of "Category: Modding Tools". Then open **Filters**, choose another category, and press Escape; open it again, choose another category, and **Close**. Open it again, **Reset**, and **Apply**. Then apply **Week** under "Updated within" again, and **Clear all**.
  The × searches again at once, without the category, and the chip goes ("Filters · 1"). Escape and **Close** close the card without searching, and it opens again on what's applied. **Reset** puts the card back to the order Browse starts on, any time, all categories, no author, adult content left out and 20 per page (or Stardew Valley's own Browse settings, when it has some), searching only on **Apply**. **Clear all** removes every chip, keeping the order and page size, in one search.
- [ ] **3.3d Each game keeps its own.** Apply **Modding Tools** for Stardew Valley, choose another game, then Stardew Valley again.
  The other game starts on every category, with its own. Back on Stardew Valley, its rows show, and **Filters** and the chip still say Modding Tools; searching uses it. After a restart, it starts afresh.
- [ ] **3.4 Author.** In **Filters**, type `Pathoschild` in the author box, and press Enter.
  Only Pathoschild's mods, with the chip "Author: Pathoschild".
- [ ] **3.5 A mod's page.** Click the × of the author chip, search `smapi`, and click SMAPI.
  Its picture across the top (with **Cards**), its name, then "by Pathoschild", "Modding Tools", its version, endorsements, downloads and last update, and its summary. **Open on Nexus Mods**, **Watch** and **Track on Nexus Mods** (Premium). "Files on Nexus Mods" lists its main file with **Download** and **Save to a folder…**; **Show *N* more files: old versions, …** lists the old versions. Beside them (below them in a narrow window), "Changelog" shows the latest three versions, newest first, with **All *N* entries** for the rest, and "Details" says "Mod ID 2400" and "Category Modding Tools".
- [ ] **3.6 A link in the search box.** **Back to the list**, paste `https://www.nexusmods.com/stardewvalley/mods/2400` into the search box, and **Search**.
  SMAPI's page opens.
- [ ] **3.7 Links it refuses.** Paste each, and **Search**:
  - `https://www.nexusmods.com/stardewvalley/mods/999999999`: "The site has no such mod."
  - `https://example.com/file.zip`: "No site ModShelf knows has mods at that link."
- [ ] **3.8 Previews.** **Back to the list**, search `smapi` again, and look at the results with each of **Cards**, **Thumbnails** and **Rows** in the view switch beside **Filters**.
  Each view's icon names it in a tooltip. **Cards**, the default: each mod is a card with its picture across the top, as many side by side as fit; clicking one opens it. **Thumbnails**: the list, with each mod's thumbnail beside it. **Rows**: the list without pictures. A mod without a picture shows a faint picture sign. The choice stays after closing and starting the app.
- [ ] **3.9 Adult pictures.** Choose Skyrim Special Edition, tick **Include adult content** in **Filters** and **Apply**, and search `outfit`.
  A chip says "Adult content included". The adult mods' pictures are blurred, and their cards say "Adult · blurred". No sharp adult picture shows, even for a moment.
- [ ] **3.10 Select mods.** Choose Stardew Valley, search `smapi`, and tick the boxes of two mods (at the top left of each card; with **Rows**, beside each name).
  "2 selected", with **Download selected (2)** and **Clear selection**, and **Select all shown** is half ticked. Clicking a box doesn't open its mod. Ticking **Select all shown** selects every mod listed; **Load more** keeps them, and **Select all shown** is half ticked again. **Clear selection** clears them all, and a new search starts with none selected. Don't download them here: 9.7 does.

## 4. Download into the library (Premium only)

- [ ] **4.1 From a mod's page.** On SMAPI's page, **Download** on its main file.
  A note says where it downloads, and that it shows on the Downloads screen. The Downloads screen shows it with its progress, speed and time left, then "Done".
- [ ] **4.2 In the library.** Open the Library screen.
  "1 game · 1 mod · *size* on disk" under the heading. Stardew Valley's row: a cover with its banner from Nexus Mods ("S" on a colour for a moment, until it arrives), "1 mod", "Up to date", the file's size, and in "Checked" when it was last checked for updates, or "Not yet". Click it: "Library › Stardew Valley", its cover and "1 mod · Up to date · *size* on disk" at the top, then SMAPI's row: its name, "by Pathoschild", your version (the file's), no status, "1 file", the size, and when Nexus Mods last updated it. In its **…** menu, **Open folder** opens SMAPI's folder, `Library\nexus\stardewvalley\<SMAPI's name> (2400)` in the test folder, holding the file. **Open on its site** opens the mod's page in the browser.
- [ ] **4.3 Again.** On the Downloads screen, paste SMAPI's link (`https://www.nexusmods.com/stardewvalley/mods/2400`) and **Download**.
  "… is in the library already." Nothing new downloads.
- [ ] **4.4 An old version.** On SMAPI's page in Browse, **Show … more files**, then **Download** on an old version.
  It downloads into SMAPI's folder too. The Library shows SMAPI with 2 files.
- [ ] **4.5 Delete a file.** Open SMAPI's page from the Library screen. In "Your files", open the old version's **…** menu, and **Delete…**.
  "Delete this file?", with its name and size. **Cancel** leaves it. **Delete…** again, then **Delete**: the file is gone from SMAPI's folder and from "Your files", and back in Stardew Valley's mods, SMAPI shows "1 file".
- [ ] **4.6 Pictures in the library.** On the Library screen, look at the games, then Stardew Valley's mods, with the Library's previews (Settings › General › Previews) on **Thumbnails**, the default, then **Cards**.
  The games: a small cover in each row, Stardew Valley's banner from Nexus Mods; then cards with it large, each with its name, mods, size and updates; with **Rows**, rows without covers. The mods: a thumbnail of SMAPI's picture beside its name; then a card with the picture, its name, its **…** menu, "by Pathoschild", its version, "1 file" and size. The test folder now has a `pictures` folder, beside `library.db`, with files named by long hexadecimal names. With the network off, closing and starting the app shows the same pictures of mods, and the games' initials on their colours, since Nexus Mods can't say where their banners are.
- [ ] **4.7 A library mod's page.** Click SMAPI's name on the Library screen (or **Open** in its **…** menu).
  The page opens in place of the list, under "Library › Stardew Valley › SMAPI". "Your files": SMAPI's folder, then the file with "Main", its name, size, "Downloaded *today*" and its SHA-256 shortened to its ends; the copy button beside it puts the whole SHA-256 on the clipboard. **Open folder** opens SMAPI's folder. In "Files on Nexus Mods", the downloaded file says "In the library" instead of **Download**. "Details" adds "In the library since *today*" and "Last checked". **Rescan** and **Remove from library** are under the name. **Stardew Valley** in the line at the top goes back to its mods, and **Library** to the games.
- [ ] **4.8 A file missing from disk.** Rename SMAPI's file in its folder, then open SMAPI's page again.
  The file says "Missing from disk". Name it back, **Rescan** on the page: "Rescan finished", and the file no longer says so.
- [ ] **4.9 Offline.** Turn the network off, open the Library screen's SMAPI again.
  A strip says "Nexus Mods couldn't give the mod, so its saved details show: …", with **Try again**. The page shows SMAPI's saved name and picture, and your files; "Files on Nexus Mods" and "Changelog" say "Shown once Nexus Mods gives the mod." Turn the network on and **Try again**: the strip goes, and the whole page shows.
- [ ] **4.10 Recent activity.** On the Library screen's games, **Recent activity**.
  "Library › Recent activity", then today's heading "Today", with, newest first: the old version deleted in 4.5, "Deleted … from disk and the library."; the old version downloaded in 4.4; and SMAPI downloaded in 4.1, each with its time, a tag (Downloaded, Deleted), SMAPI's name and "Stardew Valley". **Show** **Deleted files** leaves the deletion alone; **Removed mods**: "Nothing of that kind happened in the last year."; **Everything** again. Click SMAPI's name: its page opens under "Library › Recent activity › SMAPI", and **Recent activity** goes back to the list. **Library** goes back to the games. A library from before 0.9.3 starts its activity with what happens after the update.

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
  "Checked *N* mods with *N* requests. 1 new update." The line under the heading says "1 update for 1 mod · checked just now, next in 6 hours" (the default interval). Under "Stardew Valley", with its icon from Nexus Mods, "1 update" and **Update all (1)**: Content Patcher, its change of version in a blue tag ("*old* → *new*"), its file's name, "Uploaded …", and "Replaces …" the old version. The Updates screen's badge counts 1.
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
- [ ] **8.3a Pin, skip and a note.** On the Library screen, open Stardew Valley, and in Content Patcher's **…** menu, **Pin at this version**.
  Its status says "Pinned at *version*", the heading says "Up to date" without **Update all**, and the Updates screen lists nothing, its badge gone. In the menu, **Add a note…**: type two lines (Enter starts the second) and **Save**. Content Patcher's page shows "Pinned at *version*: its updates aren't offered." and the note above "Your files", and its newest main file no longer says "Update for your …". The menu's **Edit the note…** starts with it; emptied and saved, it's gone. **Unpin**: the update is back. On the Updates screen, **Skip** on it: it leaves, and Content Patcher's status says "1 update skipped"; **Offer skipped updates again** in its menu brings it back. While it's pinned, `library list` in a terminal says "yes, pinned", and lists the note under "Notes:".
- [ ] **8.3b Undo.** In Content Patcher's **…** menu, **Pin at this version**, then **Undo** on the bar at the bottom. **Unwatch** it, and wait. **Watch** it again. On the Updates screen, **Skip** its update, then **Undo**. Add a note to it and save, then empty the note and save.
  After pinning, a bar at the bottom of the screen says "Content Patcher pinned." with **Undo**; **Undo** closes it, and Content Patcher is unpinned, its update offered again. After **Unwatch**, "Content Patcher is no longer watched.", and the bar goes by itself after about ten seconds, leaving it unwatched; watching it again shows no bar. After **Skip**, "Content Patcher: update *version* skipped."; **Undo** brings the update back to the Updates screen, not skipped. Saving a note shows no bar; emptying it says "Note removed from Content Patcher.", and **Undo** brings the note back. The bar's × closes it, and a newer change takes its place.
- [ ] **8.4 Download the update.** **Update all (1)** at the top of the Updates screen.
  "Downloading into …", Content Patcher's folder, under the update. On the free account, the Downloads screen says "1 file waits for you on Nexus Mods. …": **Open the next page** opens the file's page; start it there and paste the link as in 5.4. Once done, the update leaves the list and the badge goes. The Library's Content Patcher has both files: the default rule keeps the newest 2 versions.

## 9. Watch and track

- [ ] **9.1 Unwatch.** On the Library screen, in Stardew Valley's mods, open SMAPI's **…** menu, and **Unwatch**.
  SMAPI says "Not watched"; "Watched" counts one fewer. **Check now** on the Updates screen counts one mod fewer.
- [ ] **9.2 Watch from Browse.** Open a mod of another game you haven't downloaded in Browse, such as SkyUI for Skyrim Special Edition, and **Watch**.
  "Watched for updates, in the library." The Library lists its game, and among the game's mods, it says "Watched only" and "No files".
- [ ] **9.2a The games.** On the Library screen, with the games shown.
  The heading counts 2 games. "Sort" by **Most mods** (the default), **Name**, **Most space** and **Updates first** reorders them; games that tie go by name. "stardew" in the filter leaves Stardew Valley; "zzz" says "No game's name has “zzz”." With the window narrow, each game's mods and updates go under its name, "Checked" goes, and the sort goes below the filter.
- [ ] **9.2b One site.** Look at the Library's games, an open game and its **Recent activity**, the Updates screen's games, the Downloads screen's rows, the games in Settings › Games, and a mod's page from the Library and from Browse.
  None says "Nexus Mods" beside its name, and the Library has no "Site" box: Nexus Mods is the only site in use, and direct links aren't a site. In Settings › Games, games go by their names alone.
- [ ] **9.2c The game picker.** In Browse, click the game picker, then one of the recent searches. Close the app and start it again, and click the game picker.
  "Your games" lists the library's games, the one with the most mods first, each saying "*N* mods in your library", with its cover; typing `s` lists the other games whose names have it under "Other games on Nexus Mods", "Not in your library". "Recent searches" shows the words searched in Browse so far, the newest first (`outfit`, `smapi`, …); clicking one searches it in the chosen game. After the restart they're still there.
- [ ] **9.2d Quick search.** On the Downloads screen, press Ctrl+K (⌘K on macOS), then Escape; click **Search** at the top of the sidebar. Type `stardew`, press ↓ and ↑, and Enter. Press Ctrl+K and type `smapi`, and Enter; again with `skyrim`, choosing the Skyrim Special Edition game; again with `folder`, and Enter; again with `updates`, and Enter. Press Ctrl+K once more, and click the first recent search. Collapse the sidebar and hover over its search icon.
  A box opens over the screen, the screen dimmed, with the cursor in its field: with nothing typed, the recent searches, then the screens. Escape closes it, and the keyboard is back where it was. `stardew` lists Stardew Valley under "Games", "In your library · *N* mods", with its cover, saying **Open in the Library** while highlighted; ↓ and ↑ move the highlight; Enter opens the Library at Stardew Valley's mods. `smapi` lists SMAPI under "Mods in your library", opening its page. Skyrim Special Edition, which the library no longer has after 9.5, says "On Nexus Mods" and opens Browse on it. `folder` lists settings such as "Library › Library folder", opening that category. `updates` lists the Updates screen. The recent searches are those that opened something, the newest first, and clicking one searches it again. The collapsed sidebar's icon says "Quick search (Ctrl+K)". `ui-state.json` has `"quickSearches"`.
- [ ] **9.3 Track** (Premium only). On SMAPI's page in Browse, **Track on Nexus Mods**.
  "Tracked on Nexus Mods." **Tracked mods** for Stardew Valley lists SMAPI under "*N* tracked mods of Stardew Valley on Nexus Mods.", and the website's Tracking Centre shows it.
- [ ] **9.4 Untrack** (Premium only). **Untrack on Nexus Mods**.
  "No longer tracked on Nexus Mods." It's gone from **Tracked mods** and from the website. If SMAPI was tracked before you started, track it again.
- [ ] **9.5 Remove from the library.** On the Library screen, open the **…** menu of the mod you watched in 9.2, and **Remove from library**; then **Undo** on the bar at the bottom. **Remove from library** again, and let the bar go.
  Nothing asks first: the mod leaves the Library, and so does its game, which had no other mod, and a bar at the bottom says "*name* removed from the library; its files stay in its folder." with **Undo**. **Undo** brings the mod and its game back, watched as before. The second time, the bar goes by itself after about ten seconds; **Check now** on the Updates screen counts one mod fewer. Removing a mod with files leaves them in its folder.
- [ ] **9.6 Rescan.** Copy a file of SMAPI's from its page on Nexus Mods (**Manual download**) into SMAPI's folder, then **Rescan** at the top of the Library screen.
  "Rescan finished", saying a file found on disk was added; SMAPI shows one file more, with its SHA-256. **Rescan** in SMAPI's **…** menu right after says "Nothing new".
- [ ] **9.7 Download selected.** In Browse, choose Skyrim Special Edition, search `skyui`, select the first two mods, and **Download selected (2)**.
  "Queued 2 files. They show on the Downloads screen.", and the boxes clear; a mod without a main file says instead that it couldn't be queued, and why. The Downloads screen downloads each one's newest main file into its folder in the library (on the free account, both wait for you: start them as in 5.4, or cancel them). Once they're done, select the same two again and **Download selected (2)**: "Their files are in the library already."

## 10. Settings

- [ ] **10.0 Categories and search.** Open the Settings screen, then each category in its list.
  It opens on **General**: the theme, closing the window, the previews, and pictures and mod details. **Library**: the library folder, the folder pattern, old versions and **Rescan the library**. **Updates**, **Links**, **Games** (each game's own settings, the library's games each with its banner from Nexus Mods) and **About** show theirs. Type `folder` in **Search settings**: the library folder, the folder pattern, old versions (the _archive folder), the rescan, the games' patterns and About (the log folder), under "Settings that match “folder”", and General, Updates and Links fade in the list. `zzz`: "No setting matches “zzz”." Type `stardew`: **Games** shows Stardew Valley alone, its page open. Escape, or choosing a category, ends the search. **Rescan the library** says what it found, as on the Library screen. Make the window narrow: the categories move above the settings, side by side.
- [ ] **10.1 Folder patterns.** In **Library**, under "Folder pattern", change every game's pattern to `{source}/{game}/{modid}/{file}`.
  The example under it changes as you type. `{mod}/{file}x` says what's wrong, and **Save** is disabled. With the right pattern, **Save**: a mod the library doesn't have yet then downloads to `nexus\<game>\<mod's ID>\…`. Mods in the library keep their folders, and files downloaded already stay where they are.
- [ ] **10.1a Each screen's previews.** In **General**, under "Previews", choose **Cards** for the Library, **Rows** for Browse and **Thumbnails** for Updates and Downloads, then look at each screen. Choose **Cards** in Browse's view switch, and come back. Then set them back: **Thumbnails**, **Cards**, **Rows**, **Rows**.
  Four rows: the Library ("Its games, a game's mods and a mod's page") and Browse with **Rows**, **Thumbnails** and **Cards**, Updates and Downloads without **Cards**. Each choice is kept at once, the settings file's `"previews"` holding those that aren't the default. The Library shows its games and mods as cards; Browse its results as rows, its view switch on **Rows**; Updates and Downloads their thumbnails. No screen but Browse has a previews control. Browse's **Cards** shows in Settings when it's shown again.
- [ ] **10.2 A game's own settings.** In **Games**, **Add a game…**, type `stardewvalley` and press Enter. On its page, **Override** each setting in turn: under "Folder pattern" change its pattern and **Save**; under "Old versions" choose the _archive folder; under "Update checks" choose **Every day**; under "Notices" untick the box; under "Previews" choose **Cards**; under "Browse" choose **Recently updated**, **Updated in the last week** and **50 per page**. Then **Use default** for each.
  Every setting says **Uses default** and what every game's is, until **Override** shows it, set as every game's, with **Overridden**. The pattern's example shows the game. The game's row counts the settings overridden, and the settings file holds each under `"games": { "stardewvalley": … }`. Each **Use default** takes it out; with none left, the row says "Uses the defaults" and the file holds no `games`.
- [ ] **10.2a Browse as a game says.** Override stardewvalley's Browse as in 10.2, then choose Stardew Valley on the Browse screen, and **Search**.
  The toolbar starts on **Recently updated**, **Updated in the last week** and **50 per page**, and the search lists 50 mods updated in the last week, the most recent first. Choosing another game without its own leaves the toolbar as it is.
- [ ] **10.2b Hidden games.** On stardewvalley's page, tick "Hide Stardew Valley in the Library, Updates and Browse". Then look at the Library, Updates and Browse screens, and tick **Show hidden games (1)** on the Library's games. Untick the hide box afterwards.
  The row says "Hidden". The Library's games leave Stardew Valley out until **Show hidden games (1)** is on; Updates leaves out its updates, and its badge doesn't count them; Browse's game box doesn't offer it. Its mods are still checked by **Check now**. Unticked, it's everywhere again.
- [ ] **10.2c A game's gear.** On the Library screen, open a game, then click the gear beside its name.
  The Settings screen opens on **Games**, at that game's page.
- [ ] **10.2d A game's previews.** With the Library's previews on **Thumbnails**, override Stardew Valley's previews and choose **Rows**. Open Stardew Valley on the Library screen, then another game; then choose Stardew Valley on the Browse screen and **Search**. Choose **Cards** in Browse's view switch, go back to Settings, then **Use default** on its previews.
  Stardew Valley's page shows its mods without pictures; the other game shows thumbnails. Browse shows Stardew Valley's results without pictures too, its view switch on **Rows**, with a tooltip saying a choice changes the game's, while another game shows cards. After **Cards** in Browse, Settings shows Stardew Valley's previews on **Cards**, and General › Previews still says **Thumbnails** for the Library and **Cards** for Browse. **Use default** brings back the screens' previews on both.
- [ ] **10.2e A change from the command line.** With the app open on another screen, run `dotnet run --project src/ModShelf.Cli -- settings set games.stardewvalley.notify false`, then open the Settings screen at Stardew Valley's page.
  "Notices" says **Overridden**, with the box unticked, without restarting the app.
- [ ] **10.2f A game's own folder.** With Stardew Valley's files in the library, run `dotnet run --project src/ModShelf.Cli -- library move-game stardewvalley "<a new folder>" --yes`, then look at Stardew Valley in the Library, its SMAPI page, and its page in Settings › Games. Move it back with `library move-game stardewvalley --yes` afterwards.
  No file of Stardew Valley says it's missing; **Open folder** on SMAPI opens its folder under the new folder; the folder pattern's example on Stardew Valley's Settings page starts with the new folder. **Choose another folder…** for the library moves only the other games' files, and refuses a folder that holds Stardew Valley's.
- [ ] **10.2g Add a game by its name.** In **Games**, **Add a game…**, type `starf`, and click Starfield.
  The dialog lists Nexus Mods' games but those listed already, "Type to find any of the *N* games on Nexus Mods." until you type; Starfield is added, chosen, and "Uses the defaults". Typing a key no game has, such as `notagame`, and **Add** adds it by its key.
- [ ] **10.2h A game's folder in Settings.** On Stardew Valley's page in **Games**, with its files in the library, look at "Folder", then **Choose…** a new empty folder, and **Move**. Then **Move back to the library**, and **Stop** while it moves, if it's slow enough to. Then, with a download of Stardew Valley paused on the Downloads screen, **Choose…** again. With a drive that's nearly full, such as a small USB stick, choose a folder on it.
  "Folder" says **Uses the library**, "Its *N* mods' files, *size*, are in the library's folder.", and the library's folder. The dialog, "Move Stardew Valley's files?", lists From, To, Moving (the files and their size) and, on another drive, its room; **Move** shows how far it is, closes, and the row says **Its own**, the new folder, and "Moved *N* files."; the game's row in the list counts it as overridden. **Stop** says "The move stopped" and that every file is back, with **Try again** and **OK**, and the game stays where it was. The paused download: "Stardew Valley's files can't move", saying the downloads aren't finished. The full drive: "There isn't room on E:" (on Linux and macOS, "There isn't room there"), with what the files need and what's free, and **Choose another folder…**. Closing the dialog at any step but while it moves leaves the files where they are; its × while they move stops the move first.
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
  "The diagnostics are on the clipboard. Paste them into your bug report." The report's first line is the version from 10.8. Then the system, the folders with your home folder as `~`, the settings from 10.1 to 10.5, the library's counts, the downloads by what they're doing, "nexus linked (ApiKey, Premium)" (Free for the free account), and last "Legacy arguments: none", or the deprecated calls an agent made. Nowhere does it hold your account's name, a mod's or file's name, a key or an `nxm://` link.
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
  "Unlink Nexus Mods?" **Unlink**: "Unlinked. Its stored credentials are deleted." The card says "No account is linked.", and the sidebar **Link an account**. On Windows, `account_nexus.secret` is gone from the test folder's `Secrets` folder.
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
| 1.7 The window comes back as it was | | | |
| 2.1 Card | | | |
| 2.2 Wrong key refused | | | |
| 2.3 API key page | | | |
| 2.4 Key links the account | | | |
| 2.5 Check | | | |
| 2.5a Requests running low | | | |
| 2.6 Linked from the command line | | | |
| 3.1 Games | | | |
| 3.1a Games by name | | | |
| 3.2 Search | | | |
| 3.2a The game's rows | | | |
| 3.3 Order and pages | | | |
| 3.3a Size, name and unique downloads | | | |
| 3.3a2 Endorsements | | | |
| 3.3b Time window, category and page size | | | |
| 3.3c Chips, Close and Reset | | | |
| 3.3d Each game keeps its own | | | |
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
| 4.10 Recent activity | | – | |
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
| 8.3a Pin, skip and a note | | | |
| 8.3b Undo | | | |
| 8.4 Download the update | | | |
| 9.1 Unwatch | | | |
| 9.2 Watch from Browse | | | |
| 9.2a The games | | | |
| 9.2b One site | | | |
| 9.2c The game picker | | | |
| 9.2d Quick search | | | |
| 9.3 Track | | – | |
| 9.4 Untrack | | – | |
| 9.5 Remove from the library | | | |
| 9.6 Rescan | | | |
| 9.7 Download selected | | | |
| 10.0 Categories and search | | | |
| 10.1 Folder patterns | | | |
| 10.1a Each screen's previews | | | |
| 10.2 A game's own settings | | | |
| 10.2a Browse as a game says | | | |
| 10.2b Hidden games | | | |
| 10.2c A game's gear | | | |
| 10.2d A game's previews | | | |
| 10.2e A change from the command line | | | |
| 10.2f A game's own folder | | | |
| 10.2g Add a game by its name | | | |
| 10.2h A game's folder in Settings | | | |
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
