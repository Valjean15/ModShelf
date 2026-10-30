# 34. Update links that go back in time

- Status: Accepted
- Date: 2026-10-30

## Context

A mod's update chains (ADR 0020) come from Nexus Mods' `file_updates`: pairs of an old file and the new file that
replaced it. ADR 0021's update checker takes a library file's update to be the end of its chain, and ADR 0020's rule for
old versions takes the files whose chains lead to a new file to be its old versions.

The human's first update check of a real library, 71 Dragon's Dogma 2 mods, offered four updates that were older than
the files they replaced: Bestiary 2.7.3 for 2.8.11, Mura's Armor Edits 4.7.4 for 5.2, and so on. Each mod's
`files.json` links its newest file to an archived file uploaded weeks or months before it:

| Mod | Link | Uploaded |
|---|---|---|
| Bestiary (1196) | 11143 (2.8.11, main) → 8523 (2.7.3, archived) | Oct 25 → Sep 25 |
| Bestiary (1196) | 10035 (2.8.6, main) → 7287 (2.6.3, archived) | Oct 17 → Jun 27 |
| Apex (1388) | 11461 (1.3.14, main) → 8524 (1.3.2, archived) → 7627 (1.2.15, archived) | Oct 28 → Sep 25 → Aug 14 |
| Dynamic Menu Overhaul (1641) | 11472 (2.8, archived) → 8504 (1.2.2, archived) | Oct 28 → Sep 25 |
| Mura's Armor Edits (1241) | 10262 (5.2, main) → 7682 (4.7.4, archived) | Oct 18 → Aug 23 |

The same links made the newest file an old version of the older one. Downloading the "update" would have archived the
newer file, or, under the default rule of keeping the last two, deleted the files beyond two for good.

Dynamic Menu Overhaul shows a second case. Its author uploaded 2.8 twice, archived both, and made 2.7 the main file again.
Without the backward link, the chain from 2.7 ends at the withdrawn 2.8.

## Decision

- **A link counts only forward in time** (`Replacements.Next`, in Application): a file is replaced by one the site lists
  as uploaded at the same time or later, never before. The update checker and the rule for old versions both follow
  links this way, so a file whose only link goes back is in no chain. A link to a file the site doesn't list is followed
  as before, and ends the chain.
- **An archived file is no update.** Its author withdrew it. The chain goes on past it, and the update is the newest
  file on the way that isn't archived, after the newest the library has.
- Nexus Mods' links are kept as the site gives them (`ModFile.ReplacedBy`). Believing them is Application's rule, for
  every source.

## Consequences

- A check finds no update for those four mods, and still finds the real one in the same library: Character Manager
  preset 2 → 2.1, linked forward.
- An update a check stored before stays until the site says the mod changed, since an unchanged mod isn't asked about
  again (ADR 0021). Unwatching and watching the mod again clears it: watching again needs a full check.
- Version labels are still never compared. Upload times are the site's own.

## Testing

- Application tests from the pattern in the table: a link back to an older archived file, or an older file of another
  section, isn't followed; a main file whose only link goes back is in no chain; an archived file is no update, but a
  chain goes on past it; no update is older than a file the library has further on; a newer file isn't an old version
  of an older one linked back to.
- The update finder, run on the five mods' `files.json` with the human's library files: four wrong updates before, only
  Character Manager preset 2.1 after.
