# 30. A second hardening pass

- Status: Accepted
- Date: 2026-10-29

## Context

With Phase 5 done and the human testing it by hand, they asked for the CLI and the core functions to be checked again, and for how the files are organized and how much code repeats. Eight independent reviews read the code at `94c46b6`: five for robustness (downloads; the library and update checks; accounts, secrets, settings and OS code; the mod sources; the CLI) and three for organization (core projects, UI projects, tests). Each finding was checked against the code; one that depended on outside behaviour (a library, the OS, the site) got a failing test or a run by hand first, and was dropped when it couldn't be reproduced.

The robustness findings, in short:

- **Calls and downloads that hang or go wrong.** A Nexus answer whose connection dropped part-way through its body waited for good, and a check holding its lock made every later one wait. Two files of the same name in one folder shared a partial file and were saved spliced. A resume appended whatever the server sent now, and a web page with status 200 was saved as the file. Pasting the file's own page at the CLI's `Link:` prompt hung `download`.
- **Updates missed for good.** A mod watched again, or given an older file, or one of whose files was deleted, kept its last-checked time, so the feed said "nothing new". A long check made the feed refuse a later game. A changed interval counted only after the current wait, up to 7 days.
- **Two processes.** Each process kept the credentials it read: after `account link` in the CLI, the running app kept the old key, was refused and asked for the account to be linked again. Moving the library wasn't coordinated with recording downloads into it. An update of the database that stopped part-way made every later start wait for good.
- **Files that could be lost or misplaced.** Secret files and `settings.json` were renamed into place without being flushed, so a power cut could leave them empty. A link whose account couldn't be saved left its credentials stored. Moving the library to the same folder under another name advised deleting the only copy.
- **Wrong messages.** A database that couldn't be written ended commands as "unexpected"; warnings printed stack traces; prompts went into a redirected file; a rate limit read as a failing connection; a locked keyring read as no credentials.

The human chose, before anything was built:

- **Credentials, all four:** read the store again when it may have changed; flush secret files and the settings before they replace the old ones, keep a secret write's first error, and create the encrypted store's machine ID once; put back the credentials a failed link replaced; and treat a locked keyring as an error.
- **Interfaces:** the handoff of a pasted link, and going on with a stopped job, move into Application (`ILinkDownloads.HandOverAsync`); `ILibraryStorage` gains a same-folder check. Changelogs through Application were approved too, for the refactoring that follows. Reading a mod from a link without the site was not: unwatching matches the library instead.
- **Database:** a needs-full-check flag on library mods (a new migration), and a failure reason for the rate limit. Not remembering updates announced by a check that stopped part-way, nor keeping the library's root in the database: both stay as they are.
- **Order:** these fixes first, as one set of patches; then the refactoring of the core and CLI, of Presentation, and of the desktop markup and strings, each a set of its own. The tests aren't refactored.

## Decision

### Nexus Mods

- **An answer is read whole inside each attempt's timeout** (`WholeAnswerHandler`, next to the network), so a body that stalls is a timed-out attempt, retried, then reported.
- **A server error's `Retry-After` isn't waited on** between retries, and **redirects aren't followed**: a 3xx is a refusal, rather than the account's key sent to another host, or a POST turned into a GET.
- **The rate limit's hold** lasts an hour at most from when it was seen; a 429 that shows allowance left holds back a minute, or as its `Retry-After` says; and checking a key (`validate.json`) is never held back, since the limit is each account's.
- **Links to Nexus Mods' file servers and API** (`cf-files.nexusmods.com`, `*.nexus-cdn.com`) are refused, saying what to copy instead, rather than taken as direct links whose signed address would be stored.

### Downloads

- **One file per destination:** the scheduler refuses a job for a place an unfinished job saves to already, comparing paths as the system does (`FilePaths` in Domain), and never deletes a partial file another job uses.
- **A resume continues only the file it started:** a `.part.info` file beside the partial keeps the file's length and strong entity tag or last change date, sent back as `If-Range`; an answer for a file of another length starts over.
- **A web page isn't a file:** an HTML answer fails as a link that needs refreshing, unless the file is `.html` or `.htm`.
- **Storage, not surprises:** a partial file locked by another program, or a finished one that can't be moved into place, is a storage failure; the move is tried again a few times. The free-space check follows links to the disk they lead to.
- **At start**, a job that was verifying, or downloading, whose whole file is at its destination is completed; an unreadable job row is left out and logged; a queue that can't be read is read again every 5 seconds.
- **Names** of downloads into a folder follow the library's rules (`LibraryNames.File`).
- **`DownloadFailureReason.RateLimited`** (stored as 8) says a download failed on the site's rate limit.
- **Handing over a pasted link** happens in Application: `ILinkDownloads.HandOverAsync` answers `Taken`, `NotAHandoff` (the file's link, but not the site's) or `OtherFile`. Adding a link whose job is queued already resumes or retries it, from any screen.
- **A scheduler no host started** lists the jobs as saved, as it does while another process runs the queue.

### Update checks

- **A site that can't be reached** is asked nothing more in that check, as one that refused the account is.
- **`LibraryMod.NeedsFullCheck`** is set by watching a mod again, adding another file and deleting one, and cleared by a check; such a mod is asked about directly, whatever the feed says. The migration `AddNeedsFullCheck` adds its column.
- **The feed's reach** is taken when each game's feed is asked; a feed that still refuses leaves the game's mods to be asked about one by one.
- **The interval** is read again at least every 15 minutes while waiting, counting from the same point, so a changed one counts within minutes.

### The library

- **`ILibraryStorage.IsSameFolder`** tells whether two paths lead to one folder, by putting a mark file in one and looking for it in the other; a folder it can't write in is compared by name with its links followed. Moving the library to its own folder under another name changes nothing.
- **One lock for placing, recording and moving files:** `LibraryDownloads` takes it to place a file and to record one; the mover holds it throughout (`ILibraryHold`, internal), after recording the downloads that finished but weren't recorded, and refusing to move while one can't be. Before saving the new root, the mover checks again for downloads into the old one, which another process may have started, and puts every file back if there are.
- **An old version found in its `_archive` folder** at its size, archived by a record that stopped, is listed there.
- **Path lengths:** an old version whose archived path would be too long stays where it is; a move checks every new path before moving any file.
- **Unwatching or unfollowing** a link to a library mod's page, or a file there, finds the mod in the library without asking its site, which may have removed it.

### The library database

- **Updating the database holds a lock of its own**, `<database>.update.lock`, as the download queue's lock is held. A start that finds it held waits up to 30 seconds, then says another ModShelf is updating the database. With it held, a row left in EF Core's own lock table (`__EFMigrationsLock`) is from an update that stopped part-way, and is cleared.

### Accounts and secrets

- **The credential vault reads the store again** once its copy is a minute old, and before credentials are refreshed, when a request is refused and before refreshed ones are saved. Credentials found to need linking again still do while the store has the same ones; ones kept in memory only aren't read again; a store that can't be read again keeps the ones read before.
- **A check** saves what the site said only while the account it checked is still the one linked; a link whose account can't be saved puts back the credentials the source had.
- **Writes reach the disk** before they replace the old file: secret files and `settings.json`. A secret write whose clean-up fails reports what went wrong first.
- **The encrypted store's machine ID** file is only ever created, never replaced, so two processes making it at once agree.
- **A locked keyring** (libsecret answers a lookup with nothing when its unlock prompt is dismissed or can't be shown) is told apart by searching for the item without unlocking it, and read as an error saying to unlock it.
- **On Linux the single-instance socket** goes in the user's runtime folder (`$XDG_RUNTIME_DIR`) when there's one, rather than in the shared `/tmp`.

### The CLI

- **A database that can't be changed** after it opened says so, as the startup check does, and exits 1.
- **The console** shows a warning's message on one line; the log files keep the details.
- **Prompts** go to stderr when a person types while stdout goes into a file. The API key prompt says it's hidden only when it is. A read that ends at a prompt waits a moment for Ctrl+C. Ctrl+C before anything was added says so.
- **`--to`** refuses a file followed by a separator, and on Windows a folder with a quote in it, saying how to quote it.

### Logging

| ID | Event |
|---|---|
| 11 | The download queue couldn't be read; trying again |
| 12 | A job whose file is in place is completed at start |
| 110 | A host answered with a web page instead of the file |
| 203 | A download job row can't be loaded, and is left out |
| 204 | A lock left by an update of the database that stopped part-way was cleared |
| 410 | Stored credentials couldn't be read again; the ones read before are used |
| 411 | The credentials a failed link replaced couldn't be put back |
| 412 | An account was unlinked while its credentials were refreshed |
| 616 | A download into the old root started during a move, so the files went back |
| 617 | An old version was found archived already |
| 618 | An old version's archived path would be too long |
| 706 | A feed found a question beyond its reach, so the game's mods are asked one by one |

### Testing

- **Each fix** has tests in its project, failing before the fix where the old behaviour could be shown: Nexus answers that stall, redirect or ask to retry after an hour (a handler in place of the network); two jobs for one path; resumes of a changed file and web pages; jobs in place at start; the needs-full-check flag through the database; slow checks and changed intervals on a fake clock; moves under another name, during downloads, and past the path limit; another process's account changes over the same stores; and the CLI's prompts, messages and exit codes.
- **The database's update lock:** a database one update behind, with EF Core's lock row left in it, starts and is updated; a held update lock is waited for, and then refused.
- **Reading the library while another process changes it** was suspected of failing, since the split queries aren't in a transaction. It doesn't: the first query's reader stays open, and SQLite's read lock with it, so the change waits. Tests that change the library from another connection just before the second query pin that.
- **By hand:** the locked keyring, in a private D-Bus session with GNOME Keyring locked over D-Bus and no prompter: the locked secret's read fails with the message, a missing one still reads as none, and the Secret Service tests pass. No test locks the keyring, which CI's job shares with every other test run.

## Consequences

- **The CLI's changes reach the running app within a minute**, and at once when a request is refused.
- **A library move** waits for files being placed or recorded, and placing or recording waits for the move; in one process, two files added at once no longer pick the same name.
- **A rate limit** fails a download as `RateLimited` rather than `Network`, and a pasted link that isn't the site's handoff is answered rather than added.
- **The database gains a column**, `LibraryMods.NeedsFullCheck`, false for every mod stored before.
- **A second lock file**, `<database>.update.lock`, stays beside the database, empty, like the queue's.

## Open

- **Not approved, left as they are:** updates found by a check that stops part-way aren't announced; the library's root lives only in `settings.json`; a mod can't be read from a link without its site.
- **OAuth**, off until Nexus Mods issues a client ID: a check doesn't refresh a token the site refused before its expiry; the loopback takes any request to its path as the answer; token requests can be retried or cancelled midway.
- **A locked keyring** has no automated test.
- **The refactoring** approved with these fixes comes as three sets of patches of its own: the core and the CLI (with changelogs through Application), Presentation, and the desktop's markup and strings.
