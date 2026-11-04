# 63. Games per site

- Status: Accepted
- Date: 2026-11-04

## Context

The same game on two sites stays two games, with ModShelf helping but never deciding (2026-11-03). The library and its
database already key a game by its site and its key there, but a game's own settings in `settings.json`, the hidden
games and the Settings screen's games were keyed by the key alone (`games.stardewvalley`), so two sites with a game of
the same key would share them (ADR 0062). Settings were written that way until now, when only Nexus Mods had games.

The human chose (2026-11-04):

- settings named `site/game`, such as `"games": { "nexus/stardewvalley": {…} }`, and the CLI's and the MCP server's
  `games.nexus/stardewvalley.notify` and `library.folders.nexus/stardewvalley`; a key alone, as before, read as Nexus
  Mods' game and written with its site on the next save;
- a key alone in the CLI and the MCP server still taken while it's clear which game it is;
- `GameRef` keys in Application's settings, a public change, with `GameRef`'s text form in Domain;
- the hint on a game's settings and in the Library's games, and Copy settings, which copies all of the other game's
  own settings after asking.

## Decision

- **Domain:** `GameRef.ToString()` is `site/key`, such as `nexus/stardewvalley`, and `GameRef.TryParse` reads it back:
  the site's ID before the first slash, the key after it, trimmed. A key may hold slashes; a site's ID never does.
- **Application:** `AppSettings.Games` is keyed by `GameRef`, compared by `AppSettings.GameComparer`: the same site,
  and the same key in any case, as a user types it. `GameFor`, `FoldersFor`, `OldVersionsFor`, `IntervalFor`,
  `NotifiesFor`, `ChecksUpdatesOf` and `IsHidden` take a `GameRef`; so do `ISettingsService.SetGameAsync` and
  `SetLibraryFoldersAsync`. A game keeps the key it was stored under, in its case. New:
  `CopyGameAsync(other, game)` gives the game every one of the other's own settings in place of its own, none when the
  other has none, and refuses the game itself.
- **`settings.json`:** games are written as `site/key`. One named by its key alone is Nexus Mods' game, since only
  Nexus Mods had games before 1.0.0; when both forms name the same game, the site's comes first. What else a game held
  is kept under its new name. A key that's neither is refused, saying how to name a game.
- **CLI and MCP:** `games.<site>/<game>.<setting>` and `library.folders.<site>/<game>`; `settings show` lists them so,
  and each change says the full name. A key alone names the game of that key in the library or the settings when only
  one site's is; with none, the game of the only site that has games; otherwise it's refused, naming the sites to
  choose from.
- **Desktop:** the hidden games are by site and key, so hiding one site's game hides not another's of the same key.
  The Settings screen lists games by site; two of the same name are each named with their site, as
  "Stardew Valley (Nexus Mods)". A game's page says "Also on Example Mods" when another site's listed game has its name,
  with **Copy settings from Stardew Valley (Example Mods)**, which asks first, listing what changes, or that the game
  will use every game's. The Library says "Also on …" under each of two games of the same name, and in an open game's
  heading. Names match ignoring case and spaces at their ends, until step 4's matcher. **Add a game** takes `site/key`,
  or a key alone, which is the listed game of that key when only one site's is, the only site's that has games, or the
  site the user chooses. A game's folder pattern is shown with its own site.

## Consequences

- An upgraded `settings.json` changes on the next save of any setting: each game named by its key gains `nexus/`.
  Anything that read the file's game keys itself has to read the new names.
- A script that sets `games.stardewvalley.*` keeps working while only Nexus Mods has a game of that key; once another
  site's does, it's asked to name the site.
- Only the library's games are asked their names on the Settings screen, as before, so a game only the settings have
  is named by its key, and isn't pointed out as the same as another.
- The example site's `stardew-valley` and Nexus Mods' `stardewvalley` are told apart everywhere; until step 9's chips,
  the Updates screen's headings and Browse's game box say the site only where they did.

## Testing

- Domain: the text form, read back, with slashes in the key and around spaces; text without a site or a key, or with a
  site's ID that can't be one, refused.
- Application: settings found by site and key in any case, however they were kept; the same key on two sites kept
  apart; a game's settings copied whole, none forgetting the game's own, and itself refused; the update checks, old
  versions and folders by the fake site's games.
- Infrastructure: games written by site; a key alone read as Nexus Mods' game and written with its site, keeping what
  else it held; the site's name before the key alone's; names that are neither refused.
- CLI and MCP: every game setting set, listed and reset by its full name; a key alone taken when one site's game in the
  library or the settings has it, or one site has games, and refused naming the sites otherwise; two sites' games of
  one key with settings of their own.
- Presentation: the hidden games by site; the Settings screen's games named with their sites, the hint and the copy
  choices, copying asked, saying only what the other game sets, and done, declined, and from a game without settings;
  adding a game by key with a site to choose, none chosen, with its site, and one listed already; another site's
  folder pattern previewed with its site; the Library's "Also on"; names the same ignoring case and spaces at their
  ends, and only other sites' games.
- 29 deliberate bugs, 28 caught. Two got through at first, and tests were added for them: names matched with spaces
  at their ends, and a game shown by the other's settings (`"hidden": false`) said to be hidden by copying them. The
  one left can't be caught: the hidden games start empty, and each read builds them anew with the site-and-key
  comparer, so the comparer of the empty set they start with is never used.
- By hand: `docs/manual-checklist.md` 7.5 and 7.6. The hint and Copy settings need a second site, so they're checked by
  hand with 1.0.1's.
