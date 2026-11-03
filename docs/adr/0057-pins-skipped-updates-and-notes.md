# 57. Pins, skipped updates and notes

- Status: Accepted
- Date: 2026-11-03

## Context

Every update a check found was offered: listed, counted in the badge, and downloaded by **Update all**. A user who
keeps a mod at a version on purpose, such as one another mod needs, saw its updates every day, and one who doesn't want
one update had no way to say so. The plan's step 13 adds pins, skipped updates and a note per mod, each kept in the
library database, which the MCP server reads and sets too.

The human chose (2026-11-03): pinning a mod at its version, whose updates are still found but not shown, counted or
included in **Update all**, its row saying "Pinned at 2.7"; skipping one update until a newer one; one note per mod, up
to 2,000 characters; a migration of their own; and later the same day:

- link overloads of the new `IModLibrary` methods, as `ForgetAsync` and `UnwatchAsync` have, for the CLI and MCP;
- a pinned mod's updates and skipped ones left out everywhere updates are offered, the desktop app, its tray and
  notices, `library updates`, `library check`, `library_updates` and `library_check`, while `library_list` and
  `mod_show` say pinned, note and skipped;
- `library pin|unpin <link>`, `library skip <update link> [--undo]` and `library note <link> [text] [--clear]`.

## Decision

- **Domain:** `LibraryMod.IsPinned`, `Note` (`MaxNoteLength` 2,000; trimmed, blank is none), `SkippedUpdates`, and
  `OfferedUpdates`: none for a pinned mod, and none skipped. `WithPinned`, `WithNote` and `WithUpdateSkipped`, which
  refuses a file that isn't one of the mod's updates; asking for what the mod is already returns it. A skip lasts while
  its update does: a check that no longer finds it, its file downloaded after all, or the file it replaced deleted ends
  it, and unwatching ends them all; a newer update replacing the same file is offered. The pin and the note last through
  every change, unwatching included. `Restore` takes all three, and refuses a skip of a file that isn't an update.
- **Application:** `IModLibrary.SetPinnedAsync`, `SkipUpdateAsync` and `SetNoteAsync`, each by reference or by link,
  without asking the site: a link to a mod's page, or to one of its files there, is matched in the library; an update's
  own link is matched among the library's updates, query included, before its site recognizes it. Each refuses a mod
  not in the library. `UpdateCheck.Found` leaves out a pinned mod's new updates, so notices do too.
- **Infrastructure:** the migration `AddPinsAndNotes` adds `IsPinned` (false) and `Note` to `LibraryMods`, and the table
  `SkippedUpdates` (`ModSequence`, `FileId`, both the key), which goes with its mod.
- **CLI:** `library pin`, `library unpin`, `library skip [--undo]` and `library note [text] [--clear]`, each saying what
  it did or that it was so already, failing for a mod not in the library. `library list` says "yes, pinned" in Watched,
  and lists the notes after the table, one line each. `library updates` leaves out a pinned mod's and skipped updates,
  and says how many.
- **MCP:** `library_pin` (`pinned`), `library_skip` (`skip`) and `library_note` (`note`, none to remove it), returning
  `PinResult`, `SkipResult` and `NoteResult` with the mod; `library_list`'s mods gain `pinned` and `note`, `updateCount`
  counts the updates offered, `updatesOnly` keeps those with one, and each update says `skipped`; `mod_show` gains
  `pinned`, `note` and each file's `skipped`. `library_updates` leaves both out. The instructions say so; 24 tools.
- **Presentation:** the Library's rows count and download the updates offered, say "Pinned at 2.7" (or "Pinned") and
  "1 update skipped", and their **…** menu gains **Pin at this version** or **Unpin**, **Add a note…** or **Edit the
  note…**, and **Offer skipped updates again**. The note is asked in the text dialog, which gains a starting text,
  several lines and a limit (`TextQuestion.Text`, `IsMultiline`, `MaxLength`); an empty note removes it. The Updates
  screen lists the updates offered, each with **Skip**. A mod's page shows "Pinned at 2.7: its updates aren't offered."
  and the note above its files, and an update not offered isn't called one.
- **Desktop:** the menu items, **Skip** beside **Download**, the strip on the mod's page, and a multi-line text box.

## Consequences

- An earlier ModShelf opens a database this version updated as it is (ADR 0027), leaving the pins, notes and skips
  unread; they come back with this version.
- A pinned mod's updates are still found and kept, so unpinning offers them at once, without a check.
- Skipping is by file: the next version is a new file, so it's offered. A site that replaces a file in place, keeping
  its ID, would stay skipped; Nexus Mods doesn't.
- The desktop app doesn't list skipped updates one by one: **Offer skipped updates again** offers a mod's all; the CLI
  and MCP undo one.

## Testing

- Domain: pins, offered updates, skipping and offering again, refusals, a skip through a check, a newer update, a
  download, a deletion and unwatching; the pin and note through other changes; notes trimmed, blank and too long;
  restoring and refusing.
- Application: pinning, noting and skipping by reference and by link without asking the site; an update's own link and
  one the site recognizes; refusals; a pinned mod's updates kept but not found.
- Infrastructure: a pin, a note and skips through a restart; skips changed, added and removed; skips going with their
  update and their mod; a stored skip no mod can have; a database from before.
- CLI and MCP: each command and tool, what it says, refusals; `library list`'s pinned and notes; `library updates`,
  `library_updates`, `library_list` and `mod_show`.
- Presentation: rows pinned, with and without a version, skipped once and twice, pinning and unpinning, the filter, the
  note asked, saved, removed and cancelled, offering skips again, a mod gone meanwhile, a failure; the Updates screen's
  left-out updates and **Skip**; the mod page's pin, note and updates not offered; the text dialog's new settings.
- 68 deliberate bugs, all caught but two that change nothing: ordering the skips read back, which the table's key
  orders already, and a pin result's flag taken from the request rather than the mod. Five got through at first; tests
  were added for two updates whose links differ only by the file, a pinned mod with optional files older and newer than
  its main one, offering skips again for a mod gone meanwhile, and a game whose only updates aren't offered; and the
  CLI's note dropped a step that changed nothing, a blank note being none. Five didn't build, for the analyzers, and
  were rewritten.
- By hand: screenshots of a pinned and a skipped mod with the menu, the note dialog, a mod's page with its note, and the
  Updates screen with **Skip**; `docs/manual-checklist-desktop.md` 8.3a and `docs/manual-checklist-mcp.md` 2.7.
