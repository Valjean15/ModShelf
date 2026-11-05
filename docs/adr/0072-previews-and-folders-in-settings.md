# 72. Previews and folders in Settings

- Status: Accepted
- Date: 2026-11-05

## Context

Each screen had its own previews control: the Library on its games, a game's mods and a mod's page, Updates, Downloads,
and Browse's view switch (ADR 0070). The settings keep one previews choice per screen (`previews.library`, `.browse`,
`.updates`, `.downloads`), and a game can have its own (ADR 0066), changed on its Library page while it had them.

A game could get a folder of its own (ADR 0067), but only through the command line or `settings.json`: the mover could
already plan a game's move, move it with progress, and put every file back when it stopped.

The approved mockups (step 8) moved previews into Settings, leaving Browse's view switch, named the levels Rows,
Thumbnails and Cards, and drew a game's Folder row with a dialog that says what would move before moving it.

The human chose (2026-11-05):

- General › Previews has **four rows, one a screen**, as the settings keep them; the mockup's split of the Library into
  its games, a game's mods and a mod's page would have needed new settings keys and a public change, and can come later;
- a game's folder moves **as the mockup showed**: Choose…, a dialog with what would move, then Move with progress and
  Stop; a drive without room refused; a move that stopped said; and Move back to the library.

## Decision

### General › Previews

- `ScreenPreviewsViewModel`, one per screen in `SettingsViewModel.ScreenPreviews`: the Library ("Its games, a game's
  mods and a mod's page"), Browse ("The mods a search finds, and a mod's page"), Updates and Downloads, each with
  **Rows**, **Thumbnails** and, for the Library and Browse, **Cards**. Settings that say cards for Updates or Downloads
  show as thumbnails, as those screens show them.
- A choice is saved as it's made, through `ISettingsService.SetPreviewAsync`, the default as none. Settings then reads
  the settings back, as for every choice, and `ModPictures.Show` (which was `ShowGames`) gives the screens their
  previews and the games' from them, telling the screens only when something changed. A choice that can't be saved says
  why, and the row shows what's stored.
- A search finds the card by its words: "Previews", the screens, what they cover, and the levels.

### The screens

- The Library (its games, a game's page and a mod's page), Updates and Downloads lose their previews control; the
  `PreviewsPicker` control is gone, with `PreviewsViewModel.Label` and the string for "This game's previews".
- `PreviewsViewModel` stays as each screen's previews. Browse's view switch still makes a choice: the chosen game's own
  while it has some, or else Browse's.
- The level once called **Off** is **Rows**; in a sentence, "rows" ("The screens' own: rows in the Library…"). The
  settings' value stays `off`.

### A game's Folder row

- `GameSettingsViewModel` gains `HasOwnRoot` (**Uses the library** or **Its own**), `FolderPath`, `FolderText` ("Its 73
  mods' files, 18.4 GB, are in the library's folder."), and `FolderNote` after a move ("Moved 211 files.", and the files
  that weren't on disk). The counts are the library's mods of the game that have files, and their files' sizes as
  recorded; Settings reads them with the games, each time it's shown. A move changes neither.
- **Choose…** asks for a folder ("Choose a folder for Stardew Valley's files"); the library's own folder means moving
  back. **Move back to the library** shows with a folder of its own. The folder the files are in already does nothing.
- A game without files takes the folder at once, through `SetGameAsync`, as the command line does; one with files opens
  the move dialog.
- A game's own folder counts as one of its settings in the games' list ("1 setting overridden"). Copying another
  game's settings still leaves it.

### The move dialog

- `GameMoveViewModel`, in Settings, through `IDialogService.MoveGameAsync`, goes by `GameMoveStage`:
  - **Planning:** `ILibraryMover.PlanGameMoveAsync`, saying "Finding its files…".
  - **Asking:** "Move Skyrim Special Edition's files?", with its facts (`GameMoveFact`): From, To, Moving ("211 files,
    18.2 GB", "1 file, …" or "No files"), the room on another drive ("Room on E:", or "Room there" where folders don't
    start with a drive, as on Linux and macOS), the files already there (and the copies left in the old folder), and
    the files missing from disk. **Cancel** or **Move**.
  - **NoRoom:** "There isn't room on E:", with what the files need and what's free there.
  - **Refused:** "Skyrim Special Edition's files can't move", with the mover's reason, such as a folder inside another
    game's or downloads of the game that aren't finished.
  - Both offer **Choose another folder…**, which plans again.
  - **Moving:** "Moving Skyrim Special Edition's files", a progress bar and "136 of 211 files · 11.7 GB of 18.2 GB",
    told from wherever the move runs and shown on the UI thread (`IUiThread`, which Settings now takes); **Stop**
    cancels it, the mover putting every file back.
  - **Stopped:** "The move stopped", with the mover's account of where the files are, or "Nothing moved, and … stays
    where it was." when none had; **OK** or **Try again**. A file that couldn't move, or a download started meanwhile,
    stops it too.
  - **Done:** the dialog closes; with the files there already, it closes at once.
- The dialog can't close while the files move: its × stops the move first. A failure that isn't the mover's is logged
  (event 953) and said as "Something went wrong."
- Desktop: `Views/Dialogs/GameMoveDialog`, its buttons and parts shown by stage.

## Consequences

- No settings, schema or public Application change.
- The previews choices live in one place, with Browse's switch as a shortcut; the Library's quick switch is gone, as
  approved.
- The library's own move (Settings › Library) still confirms with a question and moves without progress: `MoveAsync`
  has no progress to tell.
- `SettingsViewModel` takes an `IUiThread`; the desktop app registers one already.

## Testing

- General › Previews: the four screens as stored, with their notes and levels; a choice saved and the screens told; the
  default saved as none; no cards for Updates and Downloads, settings that say cards shown as thumbnails; a choice that
  can't be saved; Browse's choice shown again; the search.
- `ModPictures.Show`: the screens' previews told only when they change, as the games' are.
- The Folder row: the library's folder or its own, with the counts and sizes, one mod and none; a game without files
  taking a folder and going back at once; a move confirmed and shown, back to the library by its button or by choosing
  its folder; the folder the files are in, or none chosen, doing nothing; the dialog closed; files not on disk; one file
  moved and none needed; the counts following the library; the games' list counting the folder.
- The dialog: the facts, one and many; the room left out on the same drive or when it's not known; no room, then another
  folder; none chosen; a refusal; an unexpected failure logged; the progress, on the UI thread; Stop, before and after a
  file moved; a file that couldn't move, then Try again; the folder it's in already; closing; the drive's name, by
  system.
- 71 deliberate bugs: 69 caught, 7 of them rewritten to build (the compiler or the analyzers saw a field never set or
  read, or a member that could be static). One was the same code in effect: the Previews card's search words name
  "Thumbnails", which its note says already. One got through, clearing the dialog's facts before planning again, as no
  way through the dialog plans again once they're shown: the clearing is gone.
- `PreviewsViewModelGameTests` no longer check the label the removed control showed, and
  `SettingsGamePreviewsTests` expects "rows" for the level once called "no pictures".
- Looked at here, on Linux: General › Previews, the Library as cards with no control, a game's Folder row (the library's
  and its own), the dialog asking and moving back, and refused while a game's downloads aren't finished.
- By hand: `docs/manual-checklist-desktop.md` 4.6, 4.7, 9.2a, 10.0, 10.1a, 10.2d and 10.2h.
