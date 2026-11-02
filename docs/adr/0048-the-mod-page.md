# 48. The mod page

- Status: Accepted
- Date: 2026-11-02

## Context

The 0.9.3 canvas gives each mod a page of its own: its picture, details and category; the library's files of it, where
they are and whether they're still on disk; its site's files, saying which are updates for the library's; its
changelog; what it needs; and facts such as when it came into the library. Browse showed a mod's site details only,
and Library's mods had no page: their files were listed in rows. The plan's step 8 builds the page, shared by Library
and Browse. The human approved four choices:

- the page now, on today's recordings, with the recording script asking Nexus Mods' v2 API for what the page will need
  next, and requirements and v2 details once the human has recorded them (step 8b);
- a new source capability for requirements, `IModRequirements`, which sources implement when their site says;
- a requirement that's a mod opens its page in ModShelf; anything else opens in the browser;
- `ModInfo.Category`, which Nexus Mods' v1 API gives as an ID, named from the game's categories.

## Decision

- **Sources:**
  - `ModInfo` gains `Category`, the name of the mod's category on its site, or null.
  - `IModRequirements : IModSource`, with `GetRequirementsAsync(ModRef)`, gives `ModRequirement`s: a name, the mod
    when it's on the same site (`Mod`), the page of something elsewhere (`PageUrl`), and the site's notes. No source
    implements it yet; Nexus Mods will in step 8b.
  - Nexus Mods reads a mod's `category_id` and names it from `v1/games/{game}.json`'s categories, read the first time
    one of the game's mods is described (or with the game), and kept while the app runs. A game that can't be read
    leaves the category out, and is asked again with the next mod.
- **Application:** `ModPage` gains `Requirements`, empty by default. `ModLookup` asks the source's `IModRequirements`,
  when it has one, at the same time as the catalog's four questions.
- **Presentation**, in `ViewModels/ModPages/` (moved from Browse), shared by Library and Browse:
  - `ModPageViewModel` adds the mod's picture as the screen's previews say, its category, the latest three changelog
    entries with **All N entries**, newest first (the site's order, turned round when it lists the mod's current version
    last and not first; versions are only matched, never compared), requirements, the library's files
    (`OwnFileViewModel`), each site file's update note from the library's updates ("Update for your 1.3", "New optional
    file"), facts (Mod ID, Category, In the library since, Last checked), **Open folder**, **Rescan** and **Remove from
    library…**, and `IsOffline`.
  - `OwnFileViewModel`: name, section, file name (in `_archive/` for an archived version), size, when it was
    downloaded, whether it's missing from disk (once the library's folder is known), and the SHA-256 shortened to its
    ends, with **Copy** for the whole of it. Archived versions show when asked.
  - `RequirementViewModel`: **Open** for a mod, which opens its page on the same screen; **Open page** for anything
    else, in the browser; "In library" when the library has files of it.
  - Library opens a mod's page in place of its list, from the mod's name or **Open** in its **…** menu, with **Back**.
    When the site can't give the mod (offline, refused, no account, a link it doesn't know), the page shows the mod's
    saved details, or the library's, with the library's files, says why in `PageProblem`, and offers **Retry**; its
    files and changelog say they show once the site gives the mod. Library changes reach the open page, for the mod
    and the mods it needs.
  - Browse and Library pass `IClipboard` to the pages, for the SHA-256s.
- **Desktop:** `Views/ModPages/ModPageView`, as on the canvas: the picture across the page (cards) or beside the name
  (thumbnails); the actions under the name; "Your files" and "Files on *site*" on the left, "Changelog",
  "Requirements" and "Details" beside them, or below them in a narrow page. Library shows it under "Library › *game*
  › *mod*" with its previews, a strip for `PageProblem` with **Try again**, and a bar while a mod is read. Library's
  rows and cards open the page from the mod's name; their menus say **Open** and **Open on its site**.
- **Recording:** `Record-NexusFixtures.ps1 -Details` records the v2 schema around a mod's details and requirements,
  and the answers to `Catalog/ModDetails.graphql` and `Catalog/ModRequirements.graphql` for a mod, one that needs
  others, and both at once. Both ask `legacyModsByDomain`, which takes mods by their game's domain name and ID, several
  at once: the first recording showed that `mods` refuses a mod ID without the game's numeric ID. A refused query's
  answer is saved anyway, and the script goes on. Neither query is used yet.

## Consequences

- Opening a library mod's page asks its site for the mod, its files and changelog, and its game the first time a mod
  of the game is opened, as Browse does; a site that can't answer leaves the saved details.
- Pages show no requirements until step 8b, and a category from v1 only for a mod read from its site: saved details
  don't keep it, so an offline page has none.
- A source with requirements implements one more interface; nothing else changes for it.

## Testing

- Sources: `ModInfo`'s category and `ModRequirement`; Nexus Mods' category from the recorded game, the game read once
  for its mods and with the game, a game that can't be read then can, and a category it doesn't list.
- Application: `ModLookup` with a source that gives requirements, one that can't, and one without the capability.
- Presentation: the page's own files (missing, archived, folder, SHA-256 and copying it), update notes, facts,
  changelog order and **All N entries**, requirements (in the library, opening a mod, opening a page elsewhere),
  rescan, remove, open folder and pictures per previews; Library's page from a row, back, offline from saved or the
  library's details then **Retry**, a failure that isn't the site's, a requirement opened on the screen, and library
  changes on the open page.
- Desktop: screenshots on Xvfb over a seeded library, with a build that makes the pages up from the saved details:
  thumbnails and cards, dark and light, offline, a file missing from disk, all changes and more files, a requirement
  opened, Browse's page, and a narrow window.
- 40 deliberate bugs in the category, requirements, the page and Library's opening, each caught by a test.
- By hand: `docs/manual-checklist-desktop.md` 3.5, 4.7, 4.8, 4.9 and 8.2a.
