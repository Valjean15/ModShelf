# 51. Updates by game, and downloading them all

- Status: Accepted
- Date: 2026-11-02

## Context

The Updates screen listed the updates by mod, each with **Download**, and **Changes** to read its version's entry in
the mod's changelog. The Downloads screen listed every download, and a download a free Nexus Mods account must start
on the website showed its own **Open on Nexus Mods**. The 0.9.3 canvas groups the updates by game, shows each one's
changelog entry under it, and has **Update all** at the top and on each game's heading, in the Library too; the
Downloads screen gains a line that says what's under way, tabs by state, and a strip for the files waiting for the
user with **Open the next page**. The plan's step 10 adds these, then selecting mods in the Library and Browse, and an
MCP download of several links. The human chose (2026-11-02):

- step 10 in two parts: 10a, Updates by game, Update all and the Downloads screen; 10b, selection and the MCP batch;
- changelog entries shown when the screen opens, read a few mods at a time and kept while the app runs;
- **Open the next page** opens one page a click, never by itself;
- for 10b, the Library's selection downloads the selected mods' updates.

## Decision

- **Shown screens:** `ScreenViewModel.OnShown()` is called on the UI thread each time the main window shows a screen,
  once per change of screen (a sidebar that clears its selection and picks it again doesn't count).
- **Updates screen:**
  - `Games`: `UpdateGameViewModel`s by name, each with its cover (`Covers`, shared with the Library), `CountText`,
    `UpdateAllText` ("Update all (2)", the updates with a link) and `UpdateAllCommand`, and its updates by mod name,
    then the newest first. `Updates` stays the flat list, in the same order. A game goes by its key until its site
    names it, asked once while the app runs (`GameNames`, shared with the Library).
  - `SummaryText`: "3 updates for 2 mods · checked 2 hours ago, next in 4 hours", or "No updates · …"; the next check
    is the background check's, an interval after the oldest check (`settings.UpdateInterval`, read when the screen
    starts and each time it's shown); without readable settings it's left out; "some watched mods haven't been checked
    yet" when one never was. `Dates.In` writes "in 4 hours", "in 25 minutes", "in a moment", to the nearest minute.
  - `UpdateAllCommand` and a game's: each update with a link, one after the other, as **Download** does; the screen's
    and every game's buttons wait meanwhile (`IsUpdating`). One that fails unexpectedly is logged and says so in its
    note, and the others go on.
  - Changelogs: once the screen has been shown, each update's entry is read (`UpdateViewModel.ReadChangesAsync`), four
    at a time, once; one the site couldn't give is asked again the next time the screen is shown, as are updates found
    later. `ChangeText` ("1.3 → 1.4", "New optional file", or its version), `ChangesTitle` ("What changed in 1.4").
    **Changes** and its toggle are gone; `ReplacesText` no longer says "New optional file".
- **Library:** a game's heading has **Update all (N)** (`LibraryGameViewModel.UpdateAllCommand`, `UpdateNote`): its
  mods' updates, mod by mod by name, newest first; then "Queued 2 updates. They show on the Downloads screen.", what
  couldn't be queued and why, or that the files are in the library already (`GameUpdates`, event 944).
- **Downloads screen:** `SummaryText` ("2 downloading at 4.10 MB/s, 1 queued, 1 paused · 2 waiting for you · 1
  failed", "Nothing under way"); `Tabs` (all, under way, waiting for you, failed, finished) with counts, `SelectedTab`
  and `ShownJobs`, `NothingInTab`; `WaitingText`, `HasWaiting` and `OpenNextPageCommand`, which opens the page of the
  download that has waited longest and whose page is known. `DownloadJobViewModel` gains its speed in bytes a second.
- **Desktop:** the Updates screen as on the canvas (game headings with covers, each update's change in a tag and its
  changelog entry below), **Update all** beside **Check now**; the Library game heading's **Update all** and note;
  the Downloads screen's summary, waiting strip and tabs (`ListBox.tabs`, pills like the Library's toggles). In a
  narrow window the Updates screen's buttons and an update's **Download** go below.
- **Tests:** the Library harness waits for a game's check after opening it, and one mod page test for each change's,
  since the tests' UI thread is the check's own thread.

## Consequences

- Opening the Updates screen spends a request per mod with updates, once while the app runs; a mod's two updates share
  one. Update all on a free account queues each file to wait for the user, which the Downloads screen's strip leads
  through one page at a time.
- No interface of `Sources.Abstractions` or `Application` changed; no migration.
- Selecting mods in the Library and Browse, and the MCP download of several links, are 10b.

## Testing

- Presentation: the main window tells each screen when it's shown; games by name with their counts, named once, gone
  with their last update; a game its site can't name; the summary with the next check, from the settings read when
  shown, overdue, unreadable; one update of one mod; Update all, all and a game's, in order, waiting meanwhile, without
  links, one failing; changelogs not read before the screen is shown, four at a time, once, asked again when refused,
  for updates found later; an update's change of version; the Library's Update all, its counts, order, notes and
  failures; the Downloads summary, speeds, tabs and their counts, a tab with none, the waiting strip, one page a click,
  a page not known yet; `Dates.In`.
- 65 deliberate bugs in the orders, counts, Update all, changelogs, summaries, tabs, the strip, `OnShown`, `Dates.In`
  and game names, each caught by a test. Two survived at first and got tests: Update all's button let run again by a
  library change while it ran, and an entry read again, uselessly, each time the screen is shown. Five didn't build at
  first and were rewritten.
- By hand: screenshots of the Updates, Downloads and Library screens in both themes, with thumbnails, narrow, after
  Update all without an account, and with files waiting; `docs/manual-checklist-desktop.md` 5.1, 5.2, 7.5, 8.2–8.4.
