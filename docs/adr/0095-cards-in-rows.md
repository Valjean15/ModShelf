# 95. Cards in rows, built while they show

- Status: Accepted
- Date: 2026-11-11

## Context

The Library's games and a game's mods, and Browse's results, show as cards when the previews say so (ADR 0047): an
`ItemsControl` over a `UniformGrid`, its columns as many as fit the scroll viewer's width. A grid builds every card at
once, so a game of 450 mods built 450 cards, with their pictures, before it showed, and again on each change of order.
The lists as rows were already virtual (`ListBox`, whose `VirtualizingStackPanel` builds only the rows that show). The
plan (1.0.5, step 25) asked for the cards to be built the same way, with what Avalonia has: no new package.

## Decision

- **Cards in rows** (`CardRows`, Presentation, `ViewModels/`): a list's cards in rows of `Columns` (at least one), each
  row (`CardRow`) holding its cards and how many a row holds, so a short last row keeps each card its column's width.
  The rows follow the list as it changes, each keeping its cards that stay (`SyncWith`, as the lists themselves do), so
  a card added at the end changes only the last row.
- **A batch** (`CardRows.Batch`) follows the changes it makes once, when it ends: the Library sorts and filters its
  games and a game's mods inside one, so a list sorted again moves each card once rather than with each of the list's
  moves.
- **The view models' rows**: `LibraryViewModel.GameCards` and `ModCards`, and `BrowseViewModel.ResultCards`.
- **The views**: each card list is an `ItemsControl` over the rows in a `VirtualizingStackPanel` (`CacheLength` 1: a
  screen's worth built above and below, so scrolling and Tab find the next row ready), each row a `UniformGrid` of one
  row. **`CardColumns.For`**, set on the scroll viewer, counts the cards a row holds from its width, as
  `CardColumnsConverter` did (at least 220 wide, 16 apart), again when its width changes; the converter goes.
- Rows are 16 apart, as the grid's were; the last row has 16 below it rather than 8.

## Consequences

- A game of hundreds of mods opens as cards at once, and sorting or filtering it moves only the cards that show; the
  scroll bar's length is estimated until the rows are built, as with the lists as rows.
- A row's cards are as tall as its tallest, rather than every card as tall as the tallest of the list.
- Tab goes from card to card as before; the scroll viewer brings each into view, building the next rows.
- No public interface of Application or Sources.Abstractions changed, and no package was added.

## Testing

- Presentation: the rows of a list, at least one card to a row, the columns changed keeping the rows that stay; cards
  added, inserted, removed, moved, replaced and cleared; a card added at the end leaving the other rows alone; rows no
  longer filled dropped; a batch, one within another, and one that fails; the Library's games and a game's mods as
  cards, sorted again moving in their rows once sorted; Browse's results growing with **Load more**.
- By hand (desktop checklist 3.8a): GameBanana's Friday Night Funkin' with ten pages loaded (200 cards), scrolled to the
  end and back, narrowed and widened, and Tab through the cards; a game of 450 mods as cards in the Library, sorted
  again while scrolled halfway (under half a second), and its 61 games filtered.
