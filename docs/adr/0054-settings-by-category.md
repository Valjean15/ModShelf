# 54. Settings by category, with a search

- Status: Accepted
- Date: 2026-11-03

## Context

The Settings screen was one long column of cards: theme, closing the window, the library folder, folder patterns
(every game's, then single games'), old versions, updates, pictures and mod details, links, and About. Each step added
a card, and finding one meant scrolling. The 0.9.3 canvas and the plan's step 12 split it into categories, General,
Library, Updates, Links, Games and About, with a search box; the per-game settings follow in 12b, which gives the Games
category most of what it will hold.

The human chose (2026-11-03) to build the categories first (12a), then the per-game settings (12b).

## Decision

- **Categories:** a list beside the settings, one category shown at a time, General at first:
  - **General:** the theme, closing the window, pictures and mod details (and adult content).
  - **Library:** the library folder, every game's folder pattern, old versions, and a new **Rescan the library**, the
    Library screen's rescan, which says what it found the same way.
  - **Updates:** how often updates are checked, and notices.
  - **Links:** which apps open the links websites hand downloads over with.
  - **Games:** single games' folder patterns, with **Add a game…**.
  - **About:** this ModShelf, its updates, the diagnostics, the log folder, and where the settings are kept.
- **Search:** the words in **Search settings** show the sections that say every one of them, in a heading, an
  explanation or a choice, from every category, under "Settings that match “…”"; the case and accents don't matter.
  Categories without a match fade in the list. Nothing matching says so. Escape, emptying the box or choosing a
  category ends the search and shows the category chosen.
- **Presentation:** `SettingsCategory` (an enum), `SettingsCategoryViewModel` (its name, and whether a search matched
  any of its sections), and `SettingsSectionViewModel` (its category, the texts a search looks in, and whether it
  shows). `SettingsViewModel` gains `Categories`, `SelectedCategory`, `SearchText`, `IsSearching`, `SearchHeading`,
  `NothingMatches`, a section per card, `ClearSearchCommand` and `RescanCommand`, which needs `ILibraryRescan`. Event
  947: rescanning from the Settings screen failed unexpectedly.
- **Desktop:** the list (`ListBox.categories`) and the search box; each card shows while its section does. In a narrow
  window, the categories move above the settings, side by side, and the search box narrows.
- No setting, no file and no public interface change: the settings file is as it was.

## Consequences

- A section's search texts are the strings its card shows; a card that gains a string should give it to its section
  too, or a search won't find it by that string.
- Every game's folder pattern (Library) and single games' (Games) are now in two categories; 12b moves single games'
  into each game's own settings.
- The search looks in sections, not single settings: a match shows the whole card.

## Testing

- Presentation: the categories and their names, starting on General, each category's sections, a search across
  categories ("folder"), every word, accents and case, a heading, nothing matching, the categories faded, clearing the
  search, and one that found nothing, choosing a category ending it, a blank search, the rescan from the Settings
  screen and its failure logged.
- 23 deliberate bugs in the categories, the search and the rescan, each caught by a test. Three got through at first (a
  heading left out of the search, and the heading and "nothing matches" kept after clearing); the tests of a heading
  and of clearing a search that found nothing were added for them.
- By hand: screenshots of General, Library, Games, a search, nothing matching, light, and 760 wide;
  `docs/manual-checklist-desktop.md` 10.0, with 10.1, 10.2, 10.7 and 10.8 saying which category to open.
