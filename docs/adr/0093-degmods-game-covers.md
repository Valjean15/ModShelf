# 93. DEG Mods' game covers

- Status: Accepted
- Date: 2026-11-11

## Context

DEG Mods' games had no covers: its mods name their game by its name alone (ADR 0090), so the Library and the game
picker showed each game's initial. DEG Mods' site shows covers from the games database its admin publishes, and the
human chose (2026-11-11, plan 1.0.5, step 24) to read it.

What it is, read 2026-11-11 (the site's `index-CX1jqn5A.js`, the admin's relay, and the files):

- **The event:** kind 30078, `d` `games-db`, by DEG Mods' admin, on `relay.degmods.com`. Its `csv` tags list eleven
  files by their SHA-256, in order: `s-games-1` to `s-games-9`, `other-games` and `modded-games`.
- **The files**, on Blossom servers by their SHA-256 (`blossom.primal.net`, which sends GET to its storage elsewhere,
  and `nostr.download` had them; DEG Mods' own server answers `428`): CSV with the header `Game Name,16 by 9 image,Boxart
  image`, about 3.4 MB and 20,000 games each, 28 MB in all. Nearly all are Steam's catalog, the pictures on Steam's
  CDN (`library_hero.jpg` wide, `library_600x900.jpg` box art); `modded-games` lists three total conversions, their
  box art on `image.nostr.build`. The plan expected the pictures on Blossom, as DEG Mods' own; they aren't.
- **How the site reads them:** each file in the event's order, a later file's row for a game, by its name in
  lowercase, taking an earlier one's place.

## Decision

- **`GameCovers`** (DEG Mods' source, `Catalog/`) reads the admin's newest `games-db` event (the moderation's admin,
  `DegModsOptions.Admin`), then each file it lists, at once, from the first file server, in order and not DEG Mods'
  own, that gives it whole: its SHA-256 checked, a redirect followed (at most five, to the web, never to this
  computer's network), at most 32 MB. A file no server gives is passed over, as the site does.
- **Once a run:** the database is read the first time games are listed (`GetGamesAsync`, `GetGameAsync`), while the
  relays are, and only the rows of the games DEG Mods shows mods of are kept, for the run. A run that can't read the
  event has no covers, and reads it again the next time; once read, a game that gains its first mod later in the run
  has none until the next run.
- **Only where covers show** (`DegModsOptions.ReadsGameCovers`): the desktop app reads the database; the command line
  and its MCP server, which show no covers, don't (`AddModShelf(…, showsCovers: false)`), so `mod games` costs no
  28 MB download.
- **A game's cover** is its wide picture, or else its box art, when it's a web address not on this computer's network;
  a game without one shows its initial, as before. No icon.
- Logs 2030 to 2034, in DEG Mods' block: how many games have covers, and why the database or a file couldn't be read.

## Consequences

- DEG Mods' games show covers in the Library and the game picker, like the other sites' games.
- The first listing of DEG Mods' games in a run downloads about 28 MB (a few seconds on a fast line); its games list
  without covers until then only if the database can't be read.
- The pictures come from Steam's CDN for most games, through the app's picture cache, as other sites' pictures do.
- No public interface changed.

## Testing

- The source's tests, with the database's event signed by a test admin and its files made in the tests (DEG Mods' own
  are 28 MB): covers by wide picture or box art, a later file winning, a name in quotes and in another case; read once
  a run, from the first server giving the file whole, past a file with another hash and a server that can't be reached;
  a redirect followed; no database, then one; a file no server gives; only the admin's newest event; pictures not on
  the web, or on this computer's network, passed over; CSV fields with commas and quotes; a host that shows no covers
  reading no database.
