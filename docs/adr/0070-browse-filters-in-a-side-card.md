# 70. Browse: filters in a side card

- Status: Accepted
- Date: 2026-11-05

## Context

Browse's toolbar held every choice a search has: the order, how recently the mods were updated, the category, the
page size, the author, adult content and the previews. Each changed the search at once, so choosing three filters
searched three times, and the toolbar wrapped onto two or three lines. What was applied was only as visible as the
boxes, and nothing said which narrowed the results.

The mockups (step 8, canvas version 26) moved the filters into a card that opens from the right over the results, with
a chip per filter under the search. The human approved them, with filters applied by **Apply** and a chip's × at once.

The human chose (2026-11-05):

- each game's filters **kept while the app runs**; after a restart a game starts as its own Browse settings say, as
  before;
- adult content as **a switch, as now**, separate from how adult pictures look (`pictures.adult`), not the mockup's
  three-way box;
- chips for **the filters that narrow** only: updated within, category, author and adult content; the order and the
  page size live in the card without chips, and the results say their order;
- **Reset** puts the card back to how the game starts, applied with **Apply**.

## Decision

### The toolbar

- The game box, with the site's tracked mods' button beside it.
- The search box and **Search**, **Filters**, which counts the chips ("Filters · 3") and takes the accent's soft colour
  while there are some, and the view switch.
- **The view switch** replaces Browse's previews picker: three icons, rows, rows with thumbnails and cards, named
  "Rows", "Thumbnails" and "Cards" by their tooltips and to screen readers. They're the same levels as before (`off`,
  `thumbnails`, `cards` in the settings). The other screens keep their pickers until step 12 removes them.

### The card

- `BrowseFiltersViewModel`, the screen's `Filters`: it opens (`ToggleFiltersCommand`, the **Filters** button) on what's
  applied, offering only what the game's site searches by, in its words: the order, updated within, the category, the
  author, **Include adult content** and the page size.
- **Apply** applies the card's choices together and closes it: one search, or, when only adult content changed while
  the game's rows show, the rows listed again. Nothing changed searches nothing.
- **Close**, its ×, Escape, the **Filters** button and choosing another game close it, leaving what was applied.
- **Reset** puts the card back to how the game starts: its own Browse settings (order, updated within, page size) or
  Browse's own, every category, no author, no adult content. Settings that can't be read reset to Browse's own.
- In the view, the card is a `Border.overCard` on the right of the results, 380 wide at most; the focus goes into it as
  it opens. Updated within and the page size are pills, as the Downloads screen's tabs; the order and the category are
  boxes.

### The chips

- `Chips` holds a `BrowseFilterChipViewModel` per filter applied that the game's site searches by: "Updated in the last
  week", "Category: Armour", "Author: mireth" and "Adult content included", in that order. Its × ("Remove Category:
  Armour" to a screen reader) removes that filter and searches again at once.
- **Clear all** removes every chip's filter, keeping the order and the page size, in one search.
- The results say their order beside their count: "Order: Most endorsements" (`OrderText`), while a search is listed.

### Kept per game

- Choosing another game keeps the one left's applied filters (`BrowseFilterSet`) in memory. Choosing it again brings
  them back, as far as its site still offers them, and its own Browse settings don't start it again.
- Its category comes back as its categories are read: offered alone with every category while the site can't say
  them, and every category again when the site no longer has it.
- A game first chosen while the app runs starts as before: its own Browse settings, the order, time and page size kept
  from the game before where its site has them, and every category.

### Applying, in the screen

- The screen's `SelectedSort`, `SelectedTimeWindow`, `SelectedCategory`, `SelectedPageSize`, `IncludeAdultContent` and
  `Author` stay what's applied. Changing one in code still searches, as before; `ApplyFilters` changes them quietly,
  then searches once. Setting adult content quietly no longer lists the rows by itself.

## Consequences

- Several filters cost one search, and what narrows the results is always in view.
- The author is no longer typed in the toolbar; **Search** searches the words with the filters applied.
- A game first chosen still inherits the order, time window, page size, author and adult content of the game before,
  as it did; only a game chosen again gets its own back.
- Browse's view switch names the level "Rows"; Settings and the other screens still say "Off" until step 12.

## Testing

- The card: opening on what's applied with what the site offers; choices applied together in one search, trimmed;
  Close, the button and another game closing it without searching; nothing changed searching nothing; only adult
  content listing the rows again; Reset to the game's settings, to Browse's own, and with unreadable settings.
- The chips: each narrowing filter, in order, with their × labels and the count; a ×; each kind's ×; Clear all keeping
  the order and page size; none for what the site can't search by.
- Kept per game: filters back on a game chosen again, over its own settings; a category the site no longer has; one
  kept while the site can't say its categories.
- The order beside the results, and none for the rows or the tracked mods.
- `BrowseExampleSiteTests.AnOrderAndPageSizeItsSiteLacksBecomeItsOwnAndOnesItHasAreKept` now goes on to a Nexus Mods game
  chosen for the first time, which keeps the order and page size, and back to Skyrim Special Edition, which gets its
  own back.
- 58 deliberate bugs, all caught. Two got through at first, a game chosen again not getting back its time window, and
  getting back its page size with a search, since both games had the same: the test now gives each its own.
- Looked at here, on Linux without an account: the toolbar, the card on what's applied, Apply giving three chips and
  "Filters · 3", Escape, and a chip's ×. Updated within's pills say "Any time", "24 hours", "Week" and "Month" under
  "Updated within", and the page sizes their numbers under "Per page", so they fit a row.
- By hand: `docs/manual-checklist-desktop.md` 3.2a, 3.3 to 3.5, 3.8 to 3.10 and 10.2d. Its results table gains 3.3c and 3.3d,
  and 9.2b, which step 9 left out.
