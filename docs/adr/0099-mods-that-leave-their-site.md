# 99. Mods that leave their site

## Context

Mods disappear: an author hides or deletes one, or a site's moderators remove it. When that happens, the library's copy
may be the only one left, and ModShelf should say so and never delete it on its own. Until 1.0.8, a watched mod whose
site no longer listed its files failed every update check, as if the site were down, for as long as it stayed watched.

The human approved, for 1.0.8 (feature 2 of the plan):

- A nullable `GoneSince` on library mods, with an EF Core migration, and new `LibraryEventKind` values.
- Nexus Mods already tells "gone" from "failed": its mods' details say whether a mod is available, and the source maps
  hidden and removed mods to `ModInfo.IsAvailable == false`. No change to Sources.Abstractions was needed.

## Decision

### Finding a mod gone

- **When a check can't list a mod's files** and the site answered 403, 404 or 410, `UpdateChecker` asks the site once
  for the mod's details. A mod it describes as unavailable, or doesn't know at all (404 or 410), is **gone**: marked with
  `LibraryMod.GoneSince`, the time of the check that first found it, and counted as checked, not failed. Any other answer,
  such as a mod still described as available, or a site that can't be reached, leaves the check's failure as it was.
  A server's error (5xx) isn't a reason to ask.
- **A gone mod is asked about again weekly** (`UpdateChecker.GoneRecheck`), never through a site's feed of changed
  mods: until then, checks pass it over. Asked again and still gone, it keeps the time it first left.
- **A mod back on its site**, whose files its site lists again, loses the mark, and its updates are found again as for
  any mod.
- Marking a mod gone drops its updates, and the updates skipped: they can't be downloaded from a site that hid the mod.
  Its pin, note and files stay. An unwatched gone mod isn't checked, so it stays marked.

### Keeping what's left

- **Saved details stay:** when a site describes a mod as hidden or removed, `ModDetails` keeps the details it saved
  before (name, summary, picture, counts), marked unavailable, rather than the site's empty ones. They're read again
  as other details are, after `details.maxAge`.
- **Old versions:** keeping the latest *N* never deletes a version its site lists as archived, which may be the only
  copy left: it's moved into `_archive` instead, or left there when it's there already. A version the site no longer
  lists was never an old version (its update chain can't be followed), so it was never deleted.

### What's said

- **History:** two new `LibraryEventKind` values, `LeftSite` (5) and `BackOnSite` (6), kept like the others; the history
  stores kinds by name, so no column changes.
- **`UpdateCheck`** gains `Gone` (mods newly found gone) and `Back` (mods back), as init properties, so existing
  callers are unchanged.
- **Desktop app:** a gone mod's first status is "Gone from *site*", in the problem colour, its tooltip saying since when; the Library's
  toggles gain **Gone from their site**, offered only while a mod of the game is gone, or the toggle is on. Its page says
  that the library's copies may be the only ones left and that the details are the last ones seen. On any mod's page, a
  library file that its site no longer offers (the mod is gone or unavailable, or the site lists the file only as
  archived, or not at all) is tagged **Only copy**; a page shown without the site's files says nothing of it. Recent
  activity shows "Left its site" and "Back", and **Show** narrows to either. A background check's notice says which mods
  left their sites, after the notice of new updates, for the games that show notices.
- **Command line:** `library check` says which mods left their sites and which came back, on the output, not as
  errors; its exit code doesn't change for them. `library list` lists the mods gone from their sites, with when.
- **MCP:** `library_check` gives `gone` and `back`; `library_list` gives each mod's `goneSince`, and `goneOnly` narrows
  to them.

### Not done

- "Only copy" isn't stored: it's worked out on the mod's page from the site's list of files, read anyway. The command
  line and the MCP server say a mod is gone, which covers its files; a single file the site dropped from a mod still
  there shows only on the page.
- Only Nexus Mods has been seen to describe hidden mods. The other sources answer as they do; a mod their sites no
  longer know (404 or 410) is gone too.

## Consequences

- A hidden or removed mod no longer fails every check: it's said once, in a notice and the history, and its site is
  asked once a week, two requests, rather than at every check.
- Files that may be the last copies are marked, and never deleted by the rule for old versions.
- One migration, `AddGoneSince`, adds a nullable column to `LibraryMods`; databases of 0.9.0 to 1.0.7 open as before.
  Two new `LibraryEventKind` values and two init properties on `UpdateCheck`, approved; no new package.
- Log events 708 and 709 are new, in the update checks' block.

## Testing

- Domain: leaving a site keeps the first time and drops the updates; coming back; restoring the time.
- Application: a hidden mod gone rather than failed, with its history and log; a mod the site no longer knows gone too;
  a failure the site's description doesn't explain, or a server error, still a failure, the latter without asking; the
  weekly recheck, never through the feed, keeping the first time; a mod back losing the mark and its updates found
  again; an unwatched gone mod not checked; the notice, after the updates' one; saved details kept, marked unavailable;
  keeping the latest archiving a version archived on its site.
- Infrastructure: the time kept across a restart and cleared; a database of each release before opening as before and
  keeping a gone mod; the history keeping the new kinds.
- Presentation: the status, the toggle (hidden without a gone mod, counting and narrowing), recent activity, the mod
  page's note and its **Only copy** tags, none on a page shown without the site's files.
- Command line and MCP: a hidden mod of the test site said to have left, not failed, then passed over; listed so;
  `library_check`'s `gone` and `library_list`'s `goneOnly`.
- By hand: the command line's section 18 and the desktop app's section 20, with a mod Nexus Mods has hidden.
