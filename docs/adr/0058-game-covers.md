# 58. Games' covers

- Status: Accepted
- Date: 2026-11-03

## Context

Step 9 (ADR 0050) gave each game a placeholder cover, its initial on a colour of its own, until `GameInfo` could say
where a game's picture is. The v2 `Game` type of Nexus Mods' GraphQL API has no picture; the recordings of 2026-11-03
show the pictures behind `Query.gameArtwork`, a list of addresses with `{gameId}` in place of the game's number:
`schemaV2.hero`, the 1920×620 banner at the top of a game's page, fading to black at its foot; `schemaV2.thumbnail`, an
80×80 square; and `schemaV2.tile`, 400×600 upright. The files are WebP, whatever their names say. The v1 game the
source reads already gives the number (Stardew Valley is 1303).

The human chose (2026-11-03):

- the banner, wide, fills the Library's cards and rows, an open game's heading, and the Settings screen's games; the
  square icon goes on the Updates screen's headings; the initial on its colour stays until a picture arrives, and when
  there's none;
- `GameInfo.CoverUrl` and `IconUrl`, a public change, filled by Nexus Mods from the addresses, asked once a run, and the
  game's number; when they can't be read, games have no covers, and that's logged;
- the saved pictures only: no new column. Offline, a game's site can't say where its pictures are, so it shows its
  initial until the site answers.

## Decision

- **Sources.Abstractions:** `GameInfo(…, Uri? coverUrl = null, Uri? iconUrl = null)`, each an absolute web link or
  refused, both in `Equals`.
- **Nexus:** `GameArtwork.graphql` asks for `gameArtwork`'s addresses, once a run, the first time a game is read, shared
  by every caller and not theirs to cancel. `GetGameAsync` and `GetGamesAsync` fill `CoverUrl` from `hero` and `IconUrl`
  from `thumbnail` with the game's number; a game without a number, an address without `{gameId}` or one that isn't a
  web link gives none. When the question fails (unreachable, refused or not understood), event 502 says so once, and
  games come without pictures until the app starts again. The source takes a logger.
- **Recordings:** `Record-NexusFixtures.ps1 -Covers` records `game-artwork` from the source's own query file, beside the
  schema and the game type. The fake API matches a query without variables by its name alone.
- **Presentation:** `GameNames.ReadAsync` returns the whole `GameInfo`. `GamePictureViewModel` shows a game's picture
  from the saved pictures (`File`, `HasFile`): `Place` says where it is, `Show` and `Hide` follow the screen; a newer
  address replaces one being read, and a picture is read once while shown. Event 1574 logs a failure that isn't the
  app closing. `LibraryGameViewModel.Cover` shows the banner while the Library shows pictures, as its cover slots do;
  `UpdateGameViewModel.Icon` the icon and `GameRowViewModel.Cover` the banner always, since those slots always show.
  The Settings screen takes `ModPictures`.
- **Desktop:** each cover slot draws the picture over the initial, filled and cropped to the slot, decoded at about
  twice the width it fills: the cards, the rows, the open game's heading, the Updates headings and the Settings games.

## Consequences

- A cover costs one picture per game, saved like mods' pictures (the room they may take, **Clear**); the question of
  where pictures are costs one request a run.
- The banner is 3.1 wide to 1 high; a 16:9 card shows its middle, and its foot is dark, as on the site.
- A site that gives no pictures, or another source added later without them, keeps the initials; a source fills
  `CoverUrl` and `IconUrl` as its site allows.
- Offline at start, the games show their initials all session, since the names and pictures are asked once, as before.

## Testing

- Sources.Abstractions: a game's pictures kept and compared, refused when not web links.
- Nexus, against the recordings: a game's banner and icon, every game listed with its own, the addresses asked once
  for two games read at once and a list, one caller stopping while the others still get them, a game without a number
  or numbered 0, addresses without the number or not web links, and the question failing, logged once.
- Presentation: the picture shown once placed and shown, in either order; read once; waiting on one being read; none
  given, none downloadable, one no longer given; a newer one replacing one being read; hidden, forgetting one being read,
  and shown again; a failure logged; stopping not logged. The Library's covers from the site, the open game's, a game
  the site can't name, read once whatever the library does, arriving with a late name, none read with previews off and
  shown when turned on, and one that can't be downloaded; the Updates headings' icons whatever the previews; the
  Settings games' covers, read once and kept by their rows.
- 43 deliberate bugs, all caught. Two got through at first, and tests were added for them: a game numbered 0, and a
  caller stopping while the addresses are asked. Four didn't build, for the analyzers, and were rewritten.
- By hand: screenshots of the cards, the rows, an open game, the Updates headings, the Settings games and previews off,
  with Stardew Valley's real WebP banner and icon; `docs/manual-checklist-desktop.md` 4.2, 4.6, 8.2 and 10.0.
