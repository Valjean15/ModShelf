# Manual checklist: the installers and self-update

CI packs nothing yet and never runs an installer, so before each release someone installs ModShelf by hand on each system, then updates it from one version to the next. The checks pack two versions, 0.9.0 and 0.9.1, into a folder of your own, install the first, and let it update itself to the second from that folder rather than from GitHub. The desktop app's own flows have their checklist, [manual-checklist-desktop.md](manual-checklist-desktop.md); here, only what the installed app does differently.

The checks keep the library database, settings and credentials in a test folder, apart from your own. Each check says what to do, then what you should see. Quoted text is what the app shows.

## Before you start

You need the repository, the .NET SDK, and on each system:

- Windows: PowerShell 7 (`pwsh`), from the repository root.
- Linux: `mksquashfs` and `zstd` (the `squashfs-tools` and `zstd` packages), and a desktop with a tray host.
- macOS: a Mac; vpk packs macOS apps on a Mac only.

The installers aren't signed yet, so Windows SmartScreen and macOS Gatekeeper warn the first time: on Windows, **More info**, then **Run anyway**; on macOS, open it from Finder with Control-click, **Open**.

Pack both versions into one folder. The second packs a delta beside its full package, from the first:

```powershell
$test = Join-Path ([System.IO.Path]::GetTempPath()) 'ModShelf-installer-checklist'
$feed = Join-Path $test 'feed'
New-Item -ItemType Directory -Force $test | Out-Null
Set-Content (Join-Path $test 'notes.md') "## 0.9.1`n`n- Fixed the update check."
./build/pack.ps1 -Version 0.9.0 -Output $feed
Copy-Item (Join-Path $feed 'ModShelf.App-win-x64-Setup.exe') (Join-Path $test 'Setup-0.9.0.exe')   # on Windows
./build/pack.ps1 -Version 0.9.1 -Notes (Join-Path $test 'notes.md') -Output $feed
```

On Linux, copy `ModShelf.App-linux-x64.AppImage` to `$test/ModShelf.AppImage` instead, between the two packs; on macOS, `ModShelf.App-osx-arm64-Setup.pkg` (or `osx-x64`). `build/pack.sh` does the same in Bash: `build/pack.sh --version 0.9.0 --output "$feed"`.

The installed app reads its settings from the environment, as a build does. Started from the Start menu, the Dock or a desktop's menu, it sees only what the user's environment holds, so on Windows set these for the user until the clean-up; on Linux and macOS, start it from this terminal instead, as each section says:

```powershell
$env:ModShelf__DatabasePath = Join-Path $test 'library.db'
$env:ModShelf__SecretsFolder = Join-Path $test 'Secrets'
$env:ModShelf__SecretScope = 'ModShelf.Checklist'
$env:ModShelf__UpdateFeed = $feed
# Windows only: for the app the Start menu starts.
foreach ($name in 'ModShelf__DatabasePath', 'ModShelf__SecretsFolder', 'ModShelf__SecretScope', 'ModShelf__UpdateFeed') {
    [Environment]::SetEnvironmentVariable($name, (Get-Item "Env:$name").Value, 'User')
}
```

Run the section of the system you're on. Never paste an API key or an `nxm://` link into a report: both carry keys.

## 1. Windows

- [ ] **1.1 Install.** Run `Setup-0.9.0.exe` from the test folder.
  No administrator prompt. ModShelf starts, over the first-run setup: **Skip setup**. The Start menu lists ModShelf with its icon, three books on a shelf. The app is in `%LOCALAPPDATA%\ModShelf.App`, with `current\ModShelf.Desktop.exe` and `Update.exe`; your own `%LOCALAPPDATA%\ModShelf`, if you have one, is as it was.
- [ ] **1.2 The version and a newer one.** On the Settings screen, "About ModShelf".
  "ModShelf 0.9.0 (…)", then "ModShelf 0.9.1 is available.", its notes as text ("0.9.1", "• Fixed the update check."), **Check for updates**, **Download**, and "Look for a newer ModShelf when it starts" ticked. The sidebar's Settings has a badge, 1.
- [ ] **1.3 A notice's click.** Close the window to the tray (keep running), then **Check for updates now** in the tray menu. Click the notice.
  The notice is from ModShelf, with its icon. The window comes back on the Updates screen. Then **Quit ModShelf** from the tray, and click the same notice in the notification centre: ModShelf starts on the Updates screen.
- [ ] **1.4 Links.** On the Settings screen, **Open them with ModShelf** under "Links from websites".
  `(Get-ItemProperty 'HKCU:\Software\Classes\nxm\shell\open\command').'(default)'` starts `…\ModShelf.App\current\ModShelf.Desktop.exe`, with `--ModShelf:DatabasePath=` and the test folder's database. A **Mod manager download** on Nexus Mods brings ModShelf forward on the Downloads screen (the desktop checklist's 12.3).
- [ ] **1.5 Download.** On the Settings screen, **Download**.
  A progress bar, "Downloading ModShelf 0.9.1…", then "ModShelf 0.9.1 is downloaded. Restart ModShelf to update to it; downloads in progress pause until it's back.", with **Restart to update**. **Check for updates** is disabled.
- [ ] **1.6 Waiting.** **Quit ModShelf** from the tray, and start ModShelf from the Start menu.
  Still "ModShelf 0.9.0 (…)", with 0.9.1 downloaded and **Restart to update**: nothing updates without the user.
- [ ] **1.7 Restart to update.** **Restart to update**.
  ModShelf closes, and comes back by itself in a few seconds: "ModShelf 0.9.1 (…)", "This is the newest ModShelf.", and no badge. The Start-menu shortcut starts 0.9.1, and the links from 1.4 still open it.
- [ ] **1.8 Not at start.** Untick "Look for a newer ModShelf when it starts", quit, and start it again.
  "About ModShelf" says nothing about updates until **Check for updates**. `settings.json` in the test folder holds `"app": { "checkForUpdates": false }`.
- [ ] **1.9 Uninstall.** Quit ModShelf. In Settings > Apps > Installed apps, uninstall ModShelf.
  `%LOCALAPPDATA%\ModShelf.App` is gone; the test folder, its library and its settings are still there, and so is your own `%LOCALAPPDATA%\ModShelf`. The links went back to the app that had them before 1.4 (the command from 1.4 shows it, or `Test-Path HKCU:\Software\Classes\nxm` says False when none did), and `Test-Path` says False for `HKCU:\Software\ModShelf`, `HKCU:\Software\Classes\modshelf` and `HKCU:\Software\Classes\AppUserModelId\ModShelf.App`.

## 2. Linux

Start the AppImage from the terminal with the settings above, as `$test/ModShelf.AppImage` after `chmod +x`.

- [ ] **2.1 Start.** Run it.
  ModShelf starts, over the first-run setup: **Skip setup**. "About ModShelf" says "ModShelf 0.9.0 (…)" and "ModShelf 0.9.1 is available.", with its notes; Settings has a badge, 1.
- [ ] **2.2 Links.** **Open them with ModShelf** under "Links from websites".
  The `Exec` line of `~/.local/share/applications/modshelf-nxm.desktop` starts `…/ModShelf.AppImage` itself, not a folder under `/tmp/.mount_…`, with the test folder's database. `xdg-open` with a **Mod manager download**'s link reaches the running app.
- [ ] **2.3 Download, wait and restart.** **Download**, quit, start it again, then **Restart to update**.
  As 1.5 to 1.7: after the restart, the same `ModShelf.AppImage` runs 0.9.1, "This is the newest ModShelf."
- [ ] **2.4 Before deleting it.** **Stop opening them** under "Links from websites", then quit.
  `modshelf-nxm.desktop` is gone, and `xdg-mime query default x-scheme-handler/nxm` names the app from before 2.2. An AppImage has no uninstaller: deleting the file is all, so the links go back first.

## 3. macOS

Install the 0.9.0 `.pkg`, then start the app from the terminal with the settings above, as `"/Applications/ModShelf.app/Contents/MacOS/ModShelf.Desktop"` (or where the installer put it).

- [ ] **3.1 Start.** Run it.
  As 2.1. The Dock shows ModShelf's icon.
- [ ] **3.2 The Dock.** Close the window to the tray (keep running), then click ModShelf in the Dock.
  The window comes back.
- [ ] **3.3 Links.** **Open them with ModShelf** under "Links from websites".
  "ModShelf opens nxm:// links from Nexus Mods." A **Mod manager download** on Nexus Mods brings ModShelf forward on the Downloads screen.
- [ ] **3.4 Download, wait and restart.** **Download**, quit, start it again, then **Restart to update**.
  As 1.5 to 1.7: ModShelf.app runs 0.9.1 after the restart.
- [ ] **3.5 Before deleting it.** **Give them back** (or **Stop opening them**) under "Links from websites", then quit and move ModShelf.app to the Bin.

## 4. From 0.9 to 1.0.0

1.0.0 is the first release that isn't a pre-release, and the 0.9 copies in use must update to it. The tests open the databases and `settings.json` of each 0.9 release, and check which GitHub releases a 0.9 copy and a 1.0 copy take; here, on Windows, an installed 0.9.4 updates to 1.0.0 before the tag, and a 0.9 copy finds it on GitHub after the release is published.

After 1.9, pack 0.9.4 from its tag and 1.0.0 from the commit to be tagged, into a feed of their own, and point the test settings at it:

```powershell
$upgrade = Join-Path $test 'upgrade'
git switch --detach v0.9.4
./build/pack.ps1 -Version 0.9.4 -Output $upgrade
Copy-Item (Join-Path $upgrade 'ModShelf.App-win-x64-Setup.exe') (Join-Path $test 'Setup-0.9.4.exe')
git switch -
Set-Content (Join-Path $test 'notes-1.0.0.md') "## 1.0.0`n`n- The first stable release."
./build/pack.ps1 -Version 1.0.0 -Notes (Join-Path $test 'notes-1.0.0.md') -Output $upgrade
$env:ModShelf__UpdateFeed = $upgrade
[Environment]::SetEnvironmentVariable('ModShelf__UpdateFeed', $upgrade, 'User')
Remove-Item (Join-Path $test 'library.db*'), (Join-Path $test 'settings.json') -ErrorAction SilentlyContinue
```

- [ ] **4.1 Install 0.9.4.** Run `Setup-0.9.4.exe` from the test folder, and **Skip setup**.
  "About ModShelf" says "ModShelf 0.9.4 (…)" and "ModShelf 1.0.0 is available.", with its notes.
- [ ] **4.2 A library in 0.9.4.** Link the account; download a file of a mod; watch another mod; pin the first and add a note to it; skip an update, if one is offered; give a game a folder pattern of its own, and its previews as **Cards**; and make the theme light.
  As 0.9.4 shows them. Note the file's SHA-256 from its mod's page.
- [ ] **4.3 Update to 1.0.0.** **Download**, then **Restart to update**.
  "ModShelf 1.0.0 (…)" and "This is the newest ModShelf." Everything from 4.2 is kept: the account, still linked; the file on disk, with the same SHA-256; the watched mod; the pin, the note and the skipped update; the game's folder pattern and cards; the light theme. Settings › Updates, "Requests to the sites", says "When below 10% are left (the default)". **Recent activity** works, and the log (`logs\desktop-….log` in the test folder) has no errors.
- [ ] **4.4 From GitHub.** Once the 1.0.0 draft is published, on a computer with 0.9.4 or 0.9.5 installed from GitHub and used as usual, without the test settings: Settings, **Check for updates**.
  "ModShelf 1.0.0 is available.", with the release's notes. **Download** and **Restart to update** bring 1.0.0, with your library, settings and accounts as they were; then **Check for updates** says "This is the newest ModShelf."

## Clean up

On Windows, remove the user's settings from the start, then the test folder:

```powershell
foreach ($name in 'ModShelf__DatabasePath', 'ModShelf__SecretsFolder', 'ModShelf__SecretScope', 'ModShelf__UpdateFeed') {
    [Environment]::SetEnvironmentVariable($name, $null, 'User')
    Remove-Item "Env:$name" -ErrorAction SilentlyContinue
}
Remove-Item -Recurse -Force $test
```

Delete the AppImage or the app too, and `artifacts` in the repository.

## Results

Copy this table into the release's notes or issue, and mark each check ✅, ❌ or – (not run). For a ❌, add what you did and what the app showed, and the matching lines of the log (`logs\desktop-….log` in the test folder) and of Velopack's (`velopack_ModShelf.App.log` in the temporary folder).

| Check | Result | Notes |
|---|---|---|
| 1.1 Install | | |
| 1.2 Version and a newer one | | |
| 1.3 A notice's click | | |
| 1.4 Links | | |
| 1.5 Download | | |
| 1.6 Waiting | | |
| 1.7 Restart to update | | |
| 1.8 Not at start | | |
| 1.9 Uninstall | | |
| 2.1 Start | | |
| 2.2 Links | | |
| 2.3 Download, wait and restart | | |
| 2.4 Before deleting it | | |
| 3.1 Start | | |
| 3.2 The Dock | | |
| 3.3 Links | | |
| 3.4 Download, wait and restart | | |
| 3.5 Before deleting it | | |
| 4.1 Install 0.9.4 | | |
| 4.2 A library in 0.9.4 | | |
| 4.3 Update to 1.0.0 | | |
| 4.4 From GitHub | | |
