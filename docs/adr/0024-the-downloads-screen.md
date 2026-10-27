# 24. The Downloads screen

- Status: Accepted
- Date: 2026-10-27

## Context

Step 2 of Phase 5 (ADR 0023) fills in the Downloads screen. Until Phase 6 registers ModShelf for `nxm://` links, the screen is also where a download that Nexus Mods wants started on its website gets its link.

The human approved these shapes before they were built:

- **The screen:** a box for a link, a button that downloads it into the library, and one that saves it to a folder the user picks. The downloads are listed newest first.
- **The badge:** it counts the downloads waiting for the user, either to be started on their site or because they failed.
- **Each download:**
  - its file, its folder, where it stands, and a progress bar;
  - how much is downloaded, such as "12.3 MB of 45.6 MB", and the speed and time left, such as "2.10 MB/s, about 3 min left";
  - Pause, Resume, Retry and Cancel, as `DownloadJob` allows them.
- **While a download waits for a handoff:** "Open on Nexus Mods", and "Paste link…", which asks for the `nxm://` link. A link to another file is refused, as in the CLI.
- **A "Clear finished" button**, which removes the done and cancelled downloads from the queue. Their files stay.
- **Cancelling asks first when bytes are downloaded**, since cancelling deletes them.
- **`IDialogService` gains** `AskTextAsync(TextQuestion)` and `PickFolderAsync(title)`. Both return null when the user cancels.
- **`ScreenViewModel` gains `StartAsync`**, for work a screen does whether it's shown or not. The main window starts every screen's work, and stops it when the app closes.
- **Messages about links** say what the CLI says, from the resources.

## Decision

### The queue

**`IDownloadService.ClearFinishedAsync`** removes the completed and cancelled jobs from the queue and from the database.

- It returns how many it removed. The files stay, and the library keeps its own records.
- `IDownloadJobRepository.DeleteAsync` deletes the jobs' rows.
- The scheduler logs how many it cleared (event 7).

### Presentation

**`DownloadsViewModel`** is the screen.

- **`StartAsync` watches the queue first and lists it after**, so no change is missed. Each change reaches the list through `IUiThread`.
- **`Add` downloads the link into the library** through `ILibraryDownloads`.
  - A file in the library already is said to be there, and nothing is queued.
  - A link to a download added earlier goes on with it: a paused one resumes, and a failed one is retried.
- **`AddToFolder` asks for a folder**, then downloads the link into it through `ILinkDownloads`. When the link doesn't say what the file is called, it asks for a name.
- **What became of the link** shows under the box (`Message`), in red when the link wasn't added (`IsMessageAProblem`). A link that was added leaves the box empty.
- **`ClearFinished` clears the queue**, then removes the rows that are finished and no longer queued.
- **The badge counts the rows waiting for a handoff or failed.**

**`DownloadJobViewModel`** is one download. The screen gives it each new snapshot of its job.

- **It shows the job:**
  - its file, folder and site;
  - its status in words, and for a failure, why;
  - its progress. The bar shows no amount while the file's size isn't known, while the link is being fetched, and while the file is checked.
- **The speed is measured over the last 5 seconds** of snapshots, once there's a second to measure.
  - With the file's size, it adds the time left: less than a minute, or minutes rounded up, or hours to one decimal.
  - A download that starts again from the beginning is measured afresh.
  - Away from Downloading, no speed shows.
- **Its commands follow `DownloadJob`'s rules:**
  - Pause from Queued, Resolving, AwaitingHandoff or Downloading;
  - Resume from Paused, and Retry from Failed;
  - Cancel from anything but Completed or Cancelled.
- **Cancel asks first when bytes are downloaded.**
  - The dialog's own Cancel button keeps the download, so the confirming one says "Stop and delete".
  - The question tells how much would be deleted, and that pausing keeps it.
- **Open on the site** opens the job's handoff page. When no browser opens, a message gives the page's address. It's disabled until the queue knows the page, as just after the app starts.
- **Paste link hands the link over the way the job was added:**
  - **A job under the library root goes through `ILibraryDownloads`,** which keeps the place it was queued for.
  - **Any other job goes through `ILinkDownloads`** into its folder, under the name the link gives, as the CLI does.
  - **A link to another file is refused.** A job it added just now is cancelled, and a message names both files. Anything that isn't a link is refused too.

**What goes wrong:**

- **A link the user can act on** gets the CLI's words (`LinkProblems`). The library's and a folder's refusals differ as in the CLI.
- **A job that changed meanwhile is left as it is.** An example is one that finished just before it was paused, or one that was cleared. Its next snapshot shows what it does.
- **Anything else is logged, and the user is told it didn't work.** No command throws into the UI.
- **The link is never logged,** since it can carry a key.

**Text:**

- **Sizes are in powers of 1024,** as file managers show them, such as "512 B", "1.50 MB" or "45.6 MB".
- **Numbers are written as the user's culture writes them.** The units and every sentence are in `Strings.resx`.
- `Common/Sizes` and `Common/Text` format them for every screen.

**Logging.** Presentation takes the event IDs 900–999:

| ID | Event |
|---|---|
| 900 | A screen's work stopped (ADR 0023) |
| 901 | Adding a download failed |
| 902 | An action on a download failed |
| 903 | Clearing the finished downloads failed |

### Desktop

**`DownloadsView`:**

- **The box and its two buttons sit at the top.** Enter in the box downloads into the library.
- **Under them are the message and "Clear finished".**
- **The list is a virtualized `ListBox`.** Each row shows the file, the folder and a progress bar, then the status, size and speed.
  - The buttons a download allows sit on the right. The rows share their width, so the progress bars line up.
  - A download waiting for a handoff has its two buttons under its status, which says what to do.
- **With nothing queued,** the screen says to paste a link.

**`Styles/Shell.axaml` gains styles every screen can use:** `note` for secondary text, `problem` for what went wrong, and `rows` for a list to act on rather than pick from.

**`AvaloniaDialogService`:**

- `AskTextAsync` shows a `TextDialog`, with the box focused, ready to paste into.
- `PickFolderAsync` shows the system's folder picker over the main window.

### Testing

**Presentation tests** run the screen over fakes:

- the queue, held in memory, which tells its watchers of every change before the change returns;
- answered dialogs, a browser, the settings, the sources, and a clock the test sets.

They check:

- listing and following the queue, only on the UI thread;
- the badge;
- adding, and every refusal's words;
- saving to a folder, and asking for a name;
- clearing;
- each status and failure in words;
- sizes, progress, speed and time left;
- what each status allows, and that each command reaches the queue;
- races, and unexpected failures, logged without the link;
- the cancel question;
- opening the page, and pasting links of every kind.

**The app was run on a virtual display (Xvfb)** against a local server sending a large file slowly. It:

- listed jobs in each state, newest first;
- showed the speed and time left of a download in progress;
- refused a plain link for the library, with the message in red;
- saved a link to a picked folder, and asked for a name when the link gave none;
- paused, asked before cancelling, deleted the partial file, and cleared the finished downloads while keeping their files.

**No account was linked**, so the handoff row was looked at with sample jobs in a scratch window, in the light and dark themes. The human tries the handoff live after Phase 5.

## Consequences

- **Every Phase 1 flow of the CLI's `download` now works from the desktop app.** That includes handing a Nexus Mods link over by pasting it.
- **Phase 6's `nxm://` handler can hand links to the queue directly.** The rows already follow whatever the queue does.
- **The Library and Updates screens (step 3) can reuse** `Sizes`, `Text`, the `note`, `problem` and `rows` styles, and the way this screen follows the queue.
- **Known limit: the queue doesn't stop two jobs from saving to the same file.**
  - A link pasted for a download into a folder is added under the name the link gives, as the CLI does.
  - So a link to another file of the same name, pasted for a job in the same folder, would share that file until it's cancelled.
  - It needs two different files of one name, so it's left for now.
