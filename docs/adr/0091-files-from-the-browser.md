# 91. Files from the browser

- Status: Accepted
- Date: 2026-11-11

## Context

A download a site sends to a page waits for the user (`AwaitingHandoff`, ADR 0020). For Nexus Mods, the page's
"Download with manager" hands ModShelf an `nxm://` link, and the download goes on. Every other page hands nothing back:
MediaFire's, Mega's and Google Drive's pages, the mod pages of files only DEG Mods' gated server has (ADR 0090), and a
free Nexus Mods account's "Manual download". The browser downloads the file into its own folder, and the download in
ModShelf waits for ever: what the human saw on 2026-11-11 with DEG Mods' files. Its waiting words were Nexus Mods' for
every site: "start the download on {site}, then paste the link it hands over", though there was no link to paste.

The human chose (2026-11-11, plan 1.0.5, steps 21 and 22):

- **Watch** the folder the browser saves downloads to while a download waits on a page, **and** a **Choose the
  file…** button on the waiting download.
- **Move** the file into the library, rather than copy it.
- The shapes below, the command line's `download` taking a typed path, and the MCP server's `downloads_take`.

## Decision

### Each site's words (step 22)

- **`IDownloadResolver.HandsOverLinks`**: whether a download the site sends to a page comes back as a link ModShelf
  takes. Nexus Mods `true`; DEG Mods, GameBanana, mod.io and web links `false`. The contract
  (`SourceContractTests<T>.HandsOverLinks`, `false` unless a source's tests say otherwise) checks each source's.
- A waiting download of a site that hands over links reads as before. One of another site reads "Waiting for you:
  download it from its page, and ModShelf takes the file from your downloads folder"; its button is **Open the page**,
  and it has no **Paste link**. Downloads' summary says either, or both when downloads of both kinds wait.
- The command line's prompt says the same, and asks for a path rather than a link; for Nexus Mods it adds that a file
  the browser downloads is taken too. The MCP server's `download` says the browser downloads the file, and names
  `downloads_take`.

### Taking a file (step 21)

- **`DownloadJob.TakeFile()`**: `AwaitingHandoff` → `Verifying`; then `Complete()`, as a downloaded file, or
  **`RefuseTakenFile()`**: `Verifying` → `AwaitingHandoff`, when the file isn't the one. No other transition changes.
- **`IDownloadService.TakeFileAsync(id, path)`**, returning **`TakeFileOutcome`** (`Taken`, `HashMismatch`,
  `NotFound`, `StorageError`); the path must be full. The scheduler takes it in its loop, as every command: the job
  verifies while the file is checked and moved, off the loop; its page is kept, so a refused file leaves the download
  waiting on the same page. Cancelling the download cancels the take; stopping the queue cancels takes and waits for
  them.
- **The hash to check** is asked of the source's catalog when the file is taken (`SiteFileHashes`: the SHA-256 the
  site gives for the file, or else its other hash): a waiting job doesn't store it, so no column is added, though the
  plan expected one. A file the site gives no hash for is taken unchecked, on its name or the user's choice.
- **`IFileTransfer.TakeAsync(source, expectedHash, destination)`** (Infrastructure): hashes the file first, and leaves
  one that doesn't match where it is; moves it to the destination's partial file, then to the destination, as a
  download finishes, so the library never shows half a file (a rename on the same drive; across drives the system
  copies it, then removes the original). It never overwrites a file: one already at the destination is a storage error.
- **The file is moved**, the human's choice: it leaves the browser's folder for the library, and isn't stored twice.

### Watching the browser's folder (step 21)

- **`IBrowserDownloads.WatchAsync(folder)`**, a port of Application's, implemented in Platform by
  **`BrowserFolderWatcher`** (a `FileSystemWatcher`): the complete files that appear in the folder from then on, each
  once. A file counts when it isn't empty, has no unfinished file beside it (`.crdownload`, `.part`, `.partial`,
  `.download`, `.tmp`, `.opdownload`: Chrome, Edge, Firefox, Safari, Opera), keeps its size for two seconds, and can
  be opened for reading; Firefox's empty placeholder under the final name doesn't count.
- **`BrowserFiles`** (Application, a hosted service where the download queue runs): watches only while a download
  waits on a page, and stops when none does. A file is the download's when it has the download's file name (in any
  case, with or without the number a browser adds, `mod (1).zip`), else when its SHA-256 is the one the site gives;
  only files that appear after the download started waiting count, so what the folder held before is never touched.
  A file several downloads wait for, such as one file downloaded to two folders, goes to the one that started waiting
  last: the one the user most likely just started on its page, such as the command line's while it waits. The take
  then checks the hash as above.
- **The folder** is the setting **`downloads.browserFolder`** (`AppSettings.BrowserDownloadsFolder`, a full path),
  unset by default: the system's Downloads folder, `IUserFolders.Downloads`, which the library's default root already
  uses. The approved shape had `IBrowserDownloads.DefaultFolder`; it would have been the same folder, so the port
  leaves it out. Settings › Library has a card for it (**Choose…**, **Use the system's Downloads folder**); the command
  line has `settings set downloads.browserFolder <folder>`, and the MCP server's `settings_set`.

### Where a file can be handed

- **Desktop:** **Choose the file…** on any waiting download, Nexus Mods' too, opening the browser's folder; a file that
  isn't taken says why.
- **Command line:** `download` waits as before, watching the folder meanwhile; a full path typed instead of a link is
  taken (quotes around it are trimmed).
- **MCP:** **`downloads_take(id, path)`**, reaching the desktop app's queue through its pipe when it runs (the pipe's
  message gains the path), as the other `downloads_*` tools. A relative path is refused; a file that isn't the
  download's leaves it waiting, saying so.

### Logs

- No path is logged, only file names: a path can hold the user's name. Application's scheduler logs 14–16 (taken, not
  taken, couldn't be taken), `BrowserFiles` 30–32, Infrastructure 111 (a taken file's hash), Presentation 969 (the
  setting not saved), and Platform's watcher **2100–2101**, a new block (2100–2199).

## Consequences

- A download waiting on any page can finish in the library, as if ModShelf had downloaded it: recorded, its updates
  watched, the file in its folder. DEG Mods' gated files, the reason for this step, need no more than the browser.
- A file with the download's name but other content is checked only when the site gives a hash; without one, the name
  decides, as it does for the user.
- Files appearing in the folder while nothing waits are never looked at; nor is anything already there when a download
  starts waiting. A browser saving elsewhere (asked each time, or a folder of its own) needs the setting, or the button.
- A file moved from the browser's folder is gone from there, as the human chose.
- Public interfaces changed, as approved: `IDownloadResolver.HandsOverLinks`; `IDownloadService.TakeFileAsync` and
  `TakeFileOutcome`; `IFileTransfer.TakeAsync`; `IBrowserDownloads`; `ISettingsService.SetBrowserDownloadsFolderAsync`
  and `AppSettings.BrowserDownloadsFolder`; `QueueRequest.TakeFile`. No database change.

## Testing

- Domain: the two transitions, and each status they refuse.
- Application: the scheduler's take (taken; checked with the site's SHA-256, its other hash or none; refused, unreadable
  or not moved, and waiting on its page again; a job not waiting; a path that isn't full; cancelled or stopped
  meanwhile); `BrowserFiles` (by name, with a browser's number, by hash whatever the name, the download that started waiting last, another name with no hash, a
  file it can't read, a file that isn't the one, watching only while a download waits, the setting's folder, a folder
  that can't be watched); queue requests; the setting.
- Infrastructure: `TakeAsync` (moved through the partial name from another folder, no hash into a folder it makes,
  another file's hash left in place, the site's other hash, never over a file in place, a leftover partial file
  replaced, a missing file, one that can't be moved left in place); the settings file's `downloads.browserFolder`.
- Platform: the watcher with real files in a temporary folder (an unfinished name renamed, an empty placeholder, a file
  still growing, a file given once, a folder that isn't there); the pipe's message carrying a path.
- Presentation: each site's words and buttons; Choose the file (taken, refused with its reason, cancelled); the
  Settings card.
- CLI: the prompt for each kind of site; a typed path taken or refused; the folder watched end to end; `settings`;
  `download`'s words for a page that hands nothing over, and `downloads_take` (taken, another file, a path that isn't
  full).
- Sources: the contract's `HandsOverLinks` for each.
