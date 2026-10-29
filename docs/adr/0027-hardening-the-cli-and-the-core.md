# 27. Hardening the CLI and the core

- Status: Accepted
- Date: 2026-10-29

## Context

Phase 5 had four of its six steps in place when the human asked for the command line and the core to be made rock solid before step 5, leaving a full test of the screens for later. Three ways in were chosen:

- **The CLI end to end:** every command, with the arguments and answers that go wrong.
- **Two processes at once:** the desktop app and the CLI, or two CLI commands, on one library.
- **Failure injection:** a database that can't be read or written, settings that can't be, a keyring that fails, a full disk, and a clock that moves back.

A survey of the code, and runs of the CLI, found:

- **Two processes ran the same queue.** Two `download` commands on one database each started the other's job, so one marked it Failed (Storage) while the other finished it.
- **Lost files.** Old versions were deleted before the library recorded the new file, so a database that couldn't take the change lost them for good. A moved library whose new root couldn't be saved was left at the new root, while the library looked for it at the old one.
- **Lost credentials.** A keyring that failed at startup quietly moved credentials to the encrypted-file fallback. A refreshed token that couldn't be saved was dropped, though the site may have retired the old one. The encrypted-file store kept a failed machine ID read until ModShelf restarted.
- **Background checks that stopped for good.** The update checks ended, taking the host with them, when the settings or the library couldn't be read for a reason other than bad data, when a check timed out, or when the clock moved back far enough that the wait couldn't be timed (past about 49 days). A mod checked "later than now" was asked about through the feed since that time, which missed updates.
- **Crashes instead of messages.** A database that couldn't be opened crashed the CLI with a stack trace, and closed the desktop app without a word. Settings that couldn't be opened, a keyring that refused, and anything else unexpected did the same in the CLI. Saving a job that failed stopped the download queue.
- **Wrong or missing messages.** Arguments the command line couldn't read exited 1, as failures do. `mod search --sort 9` blamed `--page`. `--to` naming a file failed only once the download started. `mod tracked` answering 404 said the site had no such mod. `account link` called a site that answered with an error unreachable. Ctrl+C said a partial file was kept when none was, and while a saved file was being recorded.
- **No log to look at.** The desktop app logged only to the console and the debugger.

The human chose, before anything was built:

- **Two processes: one runs the queue.** A lock file beside the database says which; a crash frees it. A CLI download refuses while another process holds it. The desktop app waits, starts its queue once the other ends, and the Downloads screen says so. This adds a queue-state property to `IDownloadService`.
- **A bad database: say so and stop.** The CLI names the file and the problem and exits with an error code; the desktop app shows a window saying so before closing. Nothing is moved or deleted.
- **Credentials: all three fixes.** A keyring that errors isn't swapped for files; the machine ID is read again after a failure; a refreshed token that can't be saved is kept in memory.
- **Behaviours: all three.** A desktop log file beside the database, with no new package; a finished `download --to` run again says the file is saved already, as the desktop's **Save to a folder…** does, adding an outcome to `LinkDownload`; parse errors exit 2, and an unexpected error exits 70 with a line, the details in the log.

## Decision

### Two processes

- **`IDownloadQueueLock`** (Application port) is held by the process that runs the queue: `<database>.downloads.lock`, opened with no sharing. Windows refuses a second open; on Unix, .NET takes an advisory lock. The system lets go of it when the process ends, however it ends.
- **The scheduler** takes the lock when it starts. Without it, it answers listing and watching from the database, refuses every change with a `DownloadQueueBusyException`, and tries the lock again every 2 seconds (`QueueLockRetryInterval`), starting the queue once it has it.
- **`IDownloadService`** gains `IsQueueBusyElsewhere` and `WhenQueueRunsHereAsync`.
- **The CLI's `download`** refuses before adding anything, with exit code 1. **The Downloads screen** says another process runs the downloads, lists the queue as it is, and refreshes it once the queue runs here.

### Keeping files

- **A job that can't be saved** doesn't stop the queue: it's logged, the download goes on, and the job is saved again once the database can be written.
- **Old versions** leave the library in the change that records the new file, and the disk after it. One that can't be deleted is listed again; if even that fails, it stays on disk, unlisted, and that's logged. Archived versions are still moved first, and moved back when the library refuses the change.
- **Moving the library** moves its files back when the new root can't be saved, as when a file can't be moved.
- **A finished download saved into a folder** isn't downloaded again: `ILinkDownloads.AddAsync` returns the finished job with `AlreadySaved` when the file is still there under the same name. The CLI says it's saved already and exits 0; the desktop app says so too.

### Settings

- A settings file that can't be opened, such as one another program has locked, reads as unreadable settings, naming the file. A save that fails says which file it couldn't save, as an `IOException`.
- Each save writes a temporary file of its own beside the settings, removed when the save fails, so two saves at once don't trip over each other.

### Background checks

- Whatever fails in a cycle is logged, and tried again an interval later; only stopping ends the checks.
- No check is due later than an interval from now: a later time means the clock moved back.
- A mod checked later than now is asked about on its own rather than through the feed, and a watched mod without files gets the main files uploaded since it was added.

### Credentials

- **Linux** falls back to encrypted files only when there's no keyring: libsecret isn't installed, there's no session bus, or no Secret Service on it (GLib's `g-dbus-error-quark` codes 2, 3 and 11, and connection errors in `g-io-error-quark` other than a timeout or a cancel). A keyring that's there but fails the probe is used all the same, and that's logged as an error, which the CLI shows.
- **The encrypted-file store** reads the machine ID again after a failed read.
- **Refreshed credentials** that can't be saved are kept in memory while ModShelf runs, and saved with the next refresh that can be.
- **`account link` and `unlink`** say why the keyring, Keychain or protected files refused, and exit 1: nothing was linked, or the account stays linked.

### A database that can't be opened

- **`LibraryDatabaseUnavailableException`** (Infrastructure) is thrown when the database can't be opened or migrated at startup. Its message names the file, says what's wrong from SQLite's error (not a database or damaged, used by another program, can't be opened or written, disk full) or that its folder can't be created, and what to do. The file is left as it is.
- **Every CLI command** prints that line and exits 1. **The desktop app** shows it in a window, and closes once it's closed, with exit code 1. A failure to start for any other reason shows its message and where the log is.

### Logs

- **A file logger of ModShelf's own** (Infrastructure, no new package) writes each app's log to a `logs` folder beside the database, a file a day: `desktop-20261029.log`, `cli-20261029.log`.
  - Each entry is written and flushed as it's logged: when, how bad, the category and event, the message, then the exception, indented.
  - A file that reaches 10 MB, or that another process of the app is writing, goes on in the next free one: `cli-20261029-1.log`, and so on.
  - The app's files from before the last 7 days are deleted when a day's first entry is written. Other files stay.
  - Logging never throws: when the folder or a file can't be written, the day's logs are dropped.
  - `Logging:File` in the configuration sets what's written, as `Logging:Console` does for the console.
- **The desktop app** writes what it logs already: Information and up, warnings and up from libraries.
- **The CLI** writes Information and up to its files, while its console stays at warnings, without the database's and the host's own errors, which the command's line says.

### The CLI's exit codes and messages

- **Exit codes:** 0 done, 1 failed or refused, 2 bad input, including arguments the command line can't read, 3 still waiting to be started on the site, **70 something unexpected** (EX_SOFTWARE), 130 Ctrl+C.
- **Every command's action is guarded:**
  - a database that can't be opened says so, with exit code 1;
  - Ctrl+C that a command doesn't handle itself, such as while its host starts, says "Stopped." with exit code 130;
  - anything else says in one line what went wrong and where the log is, with exit code 70, and the details are logged with the command.
- **Messages:**
  - `mod search --sort` refuses a number that's no order, listing the orders.
  - `download --to` refuses a file before anything is queued.
  - Ctrl+C during a download says that what's downloaded so far is kept; during recording, that the file is saved and is recorded the next time.
  - A saved file the library can't record says it's saved, and why it isn't recorded.
  - A download that fails unexpectedly points to the log, rather than to debug logging.
  - `mod tracked` says the site couldn't list the tracked mods when it answers with an error.
  - `account link` tells a site that asked to slow down, or answered with an error, from one it couldn't reach.

### Logging

| ID | Event |
|---|---|
| 8 | A download job couldn't be saved; it's saved again once the database can be written |
| 9 | Another process runs the download queue |
| 10 | The download queue runs in this process now |
| 202 | The library database can't be opened |
| 301 | The keyring is there but failed, and is used all the same |
| 408 | Refreshed credentials couldn't be saved, and are kept while ModShelf runs |
| 614 | An old version couldn't be deleted nor listed again, and stays on disk |
| 615 | The library's new root couldn't be saved, so its files went back |
| 1000 | The desktop app couldn't start (a new block, 1000–1099, for Desktop) |
| 1100 | A CLI command stopped on something unexpected (a new block, 1100–1199, for the CLI) |

### Testing

- **Application:** saves that fail in the scheduler; the queue lock; old versions on a library that refuses the change, or a disk that won't let one go; a library move whose root can't be saved; background checks after each failure, on a clock that moves back (a `TimeProvider` that shifts a fake clock's time over its timers); a mod checked later than now; refreshed credentials that can't be saved.
- **Infrastructure:** the lock file; settings files that can't be opened or saved, and the temporary file; a database that isn't one, or whose folder can't be created, and SQLite's errors; the log files' days, sizes, files in use, old files and unwritable folders.
- **Platform:** which store each keyring probe leads to, and each GLib error; the machine ID read again. The keyring tests ran against GNOME Keyring on a private D-Bus session, as CI runs them.
- **CLI:**
  - two real processes on one library, one of them killed;
  - every command against a database that isn't one;
  - parse errors, an unexpected error and Ctrl+C through the guard;
  - a keyring that refuses to store or remove;
  - a download while another program holds the database, and onto a full disk (`/dev/full`, Linux only);
  - each message above.
- **By hand:** the CLI with no session bus, a bare bus, a keyring that fails to start and GNOME Keyring; the desktop app with a database that isn't one, on a virtual display (Xvfb), which showed the window, closed with exit code 1, and logged to its file.

## Consequences

- **One process runs the downloads of a library.** A CLI `download` while the desktop app is open is refused; adding the link in the app works.
- **A database locked by another program** delays the queue: saves wait for it, up to SQLite's busy timeout of 30 seconds, and then fail, which is logged, and the job is saved again later.
- **A keyring that fails** leaves accounts unusable until it works, rather than moving their credentials.
- **The log files** keep a week of each app's logs. They hold what's logged, which never includes tokens, keys or full download links.
- **Exit code 1 for parse errors** is now 2; scripts that checked for 1 there need to check for 2.

## Open

- **A newer database:** one migrated by a later ModShelf opens as it is; refusing it, or saying so, needs the migrations to be compared.
- **The busy timeout** isn't configurable; a lock held longer than 30 seconds fails the saves made meanwhile, which are made again later.
