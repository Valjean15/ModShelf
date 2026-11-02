# 43. New optional files

- Status: Accepted
- Date: 2026-11-02

## Context

An update check followed the files the library has: a file's update chain, or, for a main file its author didn't link,
a newer main file. A mod that's only watched got the main files uploaded since its last check. Nothing told of an
optional file the author added to a mod the library has files of: the human reported Bestiary's, which the agent only
found by looking at the mod's page.

The human approved, for 0.9.2: flag new optional files of watched mods as updates, and follow an optional file's chain
once it's downloaded. Nothing public changes: an update already carries its category and the file it replaces.

## Decision

- **A watched mod with files gets its new optional files as updates.** An optional file is new when it was uploaded
  since the mod's last check (or since it was added, if it was never checked), less the margin for clocks, the same
  time a watched-only mod's new main files are counted from; the library doesn't have it; and it's the newest of its
  chain, so of an author's two new versions only the later is listed. One whose replacement the site doesn't list,
  such as a pulled file, counts as the newest.
- **Each replaces nothing.** One that's the update of a library file's chain is that file's update instead, replacing
  it, as before.
- **It stays an update** while the site lists it and it stays new, in later checks too, which count from a later time.
  It goes once it's downloaded, as any update does, or once a newer version of it is listed, which is the new one.
- **Once downloaded**, it's a library file like any: its chain is followed, so its next version is its update.
- **The first check after 0.9.2** counts from each mod's last check, so it lists only the optional files uploaded since,
  not every optional file the library lacks.
- **Shown:**
  - MCP: each update in `library_updates`, `library_check` and `library_list` gains `kind`: `update` for a newer
    version of a library file, `newFile` for a file that replaces none. A watched-only mod's new main file is a
    `newFile` too.
  - CLI: `library check` and `library updates` say "(new optional file)" where the file it replaces would be.
  - Desktop: the Updates screen says "New optional file" in the same place.

## Consequences

- An author's new optional file shows up once, after the check that follows its upload, and stays until it's
  downloaded or replaced.
- An optional file uploaded before the last check, which the library never had, isn't listed: nothing finds the files
  a mod already had when it came into the library.
- Miscellaneous, archived and old-version files are never new files.

## Testing

- Application, the finder: an optional file uploaded since the last check, and none of another category; none uploaded
  before it; none the library has; only the newest of a new chain, and one whose replacement isn't listed; one that's a
  library file's update replacing it; one found before kept while it's listed, and replaced by its newer version.
- Application, the checker: a Bestiary-like case: found once, kept by the next check, and, once downloaded, its next
  version found as its update.
- CLI: `library updates` marks it; the MCP server gives `kind` in `library_updates`, `library_check` and
  `library_list`, checked against the tools' schemas.
- Presentation: the Updates screen's text for it.
- By hand: `docs/manual-checklist-mcp.md` 2.5.
