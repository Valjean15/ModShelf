# 52. Selecting mods, and downloading several links

- Status: Accepted
- Date: 2026-11-02

## Context

The 0.9.3 canvas gives each mod in the Library's game page and in Browse's results a box to select it, with a count
and an action for the selection: **Update selected** in the Library, **Download selected** in Browse. The plan's step
10b adds these, and has the MCP server's `download` take several links in one call, so an agent that downloads a
game's updates or a list of mods doesn't call it once a link. ADR 0051 did 10a. The human chose (2026-11-02):

- the Library's selection downloads the selected mods' updates (from 10a's questions);
- `download` takes `links`, from 1 to 50, with one result a link, in order, each saying its own problem;
- no CLI command for several links: MCP only, as the plan says;
- what a filter or a toggle hides is unselected, so what's selected is always among the mods shown.

## Decision

- **Shared batch:** `LibraryBatch` (Presentation, `ViewModels/Downloads`, beside `FolderDownloads` and
  `LinkProblems`), from 10a's `GameUpdates`: queues each link's file for the library one after the other, as
  **Download** does for one, counts those queued, those in the library already and those that couldn't be queued, and
  says so for updates ("Queued 2 updates. …") or for files ("Queued 2 files. They show on the Downloads screen.", "1
  was in the library already.", "Their files are in the library already."). Event 944 now reads "Queuing a file of mod
  {ModId} of {Game} for the library failed", since it's no longer only Update all's.
- **Library:** `LibraryModViewModel.IsSelected` and `SelectText` ("Select SkyUI", for screen readers);
  `LibraryViewModel.SelectionText` ("2 selected"), `HasSelection`, `UpdateSelectedText` ("Update selected (3)", the
  selected mods' updates with a link), `UpdateSelectedCommand`, `ClearSelectionCommand` and `AreAllShownSelected`
  (`bool?`: none, all, or some of the mods shown; setting it selects or unselects every mod shown). Update selected
  queues the selected mods' updates in the order shown, mod by mod, newest first, says so in the game's note as Update
  all does, waits while the game's Update all runs, and unselects them. A mod a filter or toggle hides, another game,
  the games, or a mod that leaves the library is unselected.
- **Browse:** `BrowseModViewModel.IsSelected`, `SelectText` and `CanSelect` (not a mod its site hides now, and with a
  page); `BrowseViewModel.SelectionText`, `HasSelection`, `DownloadSelectedText`, `DownloadSelectedCommand`,
  `IsDownloadingSelected`, `ClearSelectionCommand`, `AreAllShownSelected` (over the mods that can be selected),
  `SelectionNote` and `IsSelectionNoteAProblem`. Download selected queues each selected mod's page link in the order
  listed, which the library's download resolves to its newest main file, says so in the note, and unselects them.
  **Load more** keeps the selection; a new search starts with none.
- **MCP:** `download` takes `links` (1 to 50; none or more refused with "Give from 1 to 50 links.") and returns
  `DownloadsResult`: `Results`, one `LinkDownloadResult` a link in the order given (the download, the library path,
  already saved or queued, who runs it, or `Problem`), and `Message`, the one result's message, or "Of 3 links, 2 are
  downloading or waiting for the user, and 1 couldn't be downloaded; each result says more, in the order the links were
  given." A link that can't be downloaded no longer fails the call: its result says why, and the others go on.
- **Desktop:** a box at the start of each row in the Library's mods table and in Browse's rows, and at the top left
  of each card's picture (`CheckBox.select`, square corners; `Border.onPicture.box`); **Select all shown** with the
  count and the actions below the Library's toggles and beside Browse's count (`CheckBox.selectAll`). A box inside a
  card handles its own click, so selecting doesn't open the mod.
- **Code organization:** `LibraryBatch`'s log message is in `LibraryBatch.Log.cs`.

## Consequences

- The MCP `download` tool's schema changed: `link` is now `links`, an array, and its result is `DownloadsResult`. A
  script or agent prompt that called it with `link` must give `links` instead; `docs/manual-checklist-mcp.md` 2.3a
  checks several links.
- Update selected on a free account queues each update to wait for the user, as Update all does; Download selected
  the same, one page a click from the Downloads screen's strip.
- No interface of `Sources.Abstractions` or `Application` changed; no migration.

## Testing

- Presentation: the Library counts the selected mods and their updates, shown only; mods without updates to download;
  Select all shown over the mods shown; a filter, a toggle, another game, the games and a mod leaving the library
  unselect; Update selected in order, then unselected, waiting while Update all runs; Update all counting an update in
  the library already beside those queued. Browse counts the selected mods; a hidden mod can't be selected; Load more
  keeps the selection and a new search clears it; Download selected in the order listed, then unselected; the notes
  for queued, in the library, refused, one and several.
- Cli: several links in order, each with its result; two refused; none or 51 refused; every older `download` test
  through one link, its result's message the call's.
- 34 deliberate bugs in the selection's counts, Select all shown, unselecting, the batch's notes, Browse's selection
  and the MCP links, each caught by a test but two that change nothing. One survived at first and got a test: Select
  all shown selecting the mods a filter hides. The two: Select all shown's guard against its own count's change, which
  would only select what's selected already; and Browse's Select all shown going over every mod listed, since a mod
  that can't be selected unselects itself.
- By hand: screenshots of the Library's rows and cards and Browse's cards and rows with mods selected, in both themes,
  and Download selected without an account; `docs/manual-checklist-desktop.md` 3.10, 8.2d and 9.7.
