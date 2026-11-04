# 67. A folder per game

- Status: Accepted
- Date: 2026-11-04

## Context

Every file was in one folder, the library's root. Moving the library moved them all. A user keeping one game's mods on
another drive, or beside the game, couldn't.

About 60 places found a library file as the root plus its library path: placing and recording downloads, old versions,
the rescan, deleting, forgetting, opened links, the CLI, the MCP server and the desktop app's pages. Several recognized
"a download into the library" the same way.

The human chose (2026-11-04):

- the key `games.<site>/<game>.root`, which reads like `library.root` and can't be mixed up with `.folders`, the
  pattern;
- one answer to "where is this game's folder" (`LibraryRoots`), and a plan before a move, then the move with progress;
- moving:
  - a stop puts back every file moved;
  - files already in the new folder are adopted;
  - no folder nests in another;
  - a drive without room is refused;
- the desktop app's Folder row, with the warning and progress, in step 12, as planned.

## Decision

- **No database change.** A file's library path is relative to its game's folder: its own, or else the library's
  root. A game without one uses the root, as before.
- **Settings:**
  - `GameSettings.Root` is an absolute path.
  - In `settings.json` it's a game's `"root"`, first among its settings; one that isn't a full path is an error naming
    the key.
- **`LibraryRoots`, Application's public record:**
  - `Root`, and `Games`, the games with folders of their own, in any case;
  - `For(game)`, `HasOwn(game)`, `Resolve(game, path)`, `Resolve(file)` and `RelativeTo(game, path)`;
  - `Of(settings, defaultRoot)`;
  - `ISettingsService.GetLibraryRootsAsync`.

  Everything that found a file through the root, or recognized a download into the library by it, goes through it now:
  - downloads and their recording, and old versions;
  - the rescan, deleting and forgetting, and opened links;
  - the hold that records missed downloads before a move;
  - the CLI's `library list --files` and rescan;
  - the MCP server's paths;
  - the desktop app's missing files, mod pages, **Open folder**, and a game's pattern example.
- **No folder nests in another.** A game's folder can't be:
  - the library's root or another game's folder;
  - inside one of them;
  - holding one of them.

  The library's root can't be, inside or hold a game's folder either. So each file has one folder, and moving one folder
  takes no other's files along. A game may move to a folder inside its own.
- **Setting the folder** through `ISettingsService.SetGameAsync`, as the CLI and MCP do:
  - It's allowed only while the game has no files, as for `library.root`.
  - It's refused while downloads of the game into its folder aren't finished.
  - The path is stored full and trimmed, and the same folder named another way stays as stored.
  - `CopyGameAsync` leaves a game's folder as it is: it changes only with its files.
- **`ILibraryMover`:**
  - `PlanGameMoveAsync(game, folder?)` gives a `GameMovePlan`:
    - from and to;
    - how many files and bytes move;
    - how many are there already, and of those, how many still have a copy in the old folder;
    - how many are missing;
    - the room on the new folder's drive (`ILibraryStorage.FreeSpace`), and whether both are on one drive
      (`ILibraryStorage.SameDrive`);
    - `Fits`.
  - `MoveGameAsync(game, folder?, progress?)`:
    - It holds the library, plans again, and refuses a move that doesn't fit: another drive with less room than the
      files.
    - It moves file by file, telling `LibraryMoveProgress`, then stores the folder.
    - A stop, a file that can't move, a download of the game started meanwhile, or a folder that can't be saved puts
      back every file moved, and the game stays where it was.
    - A move cut off by a crash finishes when asked again: a file there at its size counts as moved.
  - A file already in the new folder at its size is adopted. Its copy in the old folder stays and is never deleted, and
    `LibraryMove.CopiesKept` says how many.
  - A null folder, or the library's root named as one, moves the game back into the library.
  - Emptied folders are removed, but never the library's root.
  - The library's own move leaves games with folders of their own where they are, and the desktop app asks only about
    the files that move.
- **CLI:**
  - `library move-game <game> [folder] [--yes]`:
    - The game is named as for `games.<site>/<game>`.
    - It says what would move, the drive and its room, the files there already, the copies that stay, and the missing
      ones.
    - Only `--yes` moves them, and without a folder it moves them back. Ctrl+C puts every file back (exit code 130).
  - `settings set games.<site>/<game>.root`, refused for a game with files, points to `library move-game`.
- **MCP:** `games.<site>/<game>.root` is in `settings_show` and its description. `settings_set` refuses it for a game
  with files, saying moving its files is for the user, with the CLI's `library move-game`. The server reads paths in
  each game's folder and moves no files.
- **Infrastructure:**
  - `FileSystemLibraryStorage.FreeSpace` and `SameDrive` use the volume that holds a folder, following links: on
    Windows its drive, elsewhere the deepest mount point.
  - Unknown means no answer for free space, and not the same drive.

## Consequences

- Until step 12, a game gets a folder of its own through the CLI or `settings.json`. The desktop app finds its files
  there, and shows the folder in the game's pattern example.
- A game's folder, like the library's root, holds only its own files. Files a user copied there are adopted, and the old
  copies stay for the user to delete.
- `LibraryMove` gained `CopiesKept`, 0 for the library's own move. `GameMovePlan.Fits` is a property beside the
  approved shape.
- `ILibraryStorage` gained two members, so every implementation and fake needs them.

## Testing

- Application:
  - `LibraryRoots`: games in any case, paths resolved and found, the settings' folders, and the clashes;
  - the settings:
    - a game's folder set while it has no files, kept while it has some;
    - other games' files not counting;
    - clashes, unfinished downloads, and paths that aren't full;
    - copying, the library's root and the move's own setter;
  - the move:
    - planning, the drives and their room;
    - the files and folder moving, with progress;
    - emptied folders, the library's root kept;
    - moving back, and the library's root named as a folder;
    - adopting, finishing after a crash, a file in the way, missing files, refusals;
    - a stop, a file that can't move, a download started meanwhile, and settings that can't be saved, each putting
      back every file;
    - the library's move leaving a game's folder, and its new root clear of one;
  - downloads, recording, old versions and the hold in a game's folder, and another game's files in another folder
    taking no name;
  - the rescan, deleting, forgetting and opened links there.
- Infrastructure:
  - a game's folder first in `settings.json`, and the wrong ones said;
  - free space for a folder not made yet, the same drive, and `/proc` on another (Linux).
- CLI and MCP:
  - `library move-game`: what it would move, moving with `--yes`, the game's downloads there, moving back, already
    there, refusals, and a badly named game;
  - the setting refused with the hint, or set and reset without files;
  - the MCP server's refusal and paths.
- Presentation:
  - missing files, **Open folder** and a mod page in a game's folder;
  - the pattern example;
  - the library's move skipping a game's files.
- 68 deliberate bugs, all caught. Four got through at first, and tests were added or made stricter:
  - another game's file in another folder taking a file's name;
  - the rescan looking for a library file in the library's root;
  - the Library counting missing files there;
  - a mod page opening its folder there.
- By hand:
  - `docs/manual-checklist.md` 7.5b;
  - `docs/manual-checklist-desktop.md` 10.2f;
  - `docs/manual-checklist-mcp.md` 3.1b.
