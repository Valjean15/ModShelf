# 65. One matcher for names

- Status: Accepted
- Date: 2026-11-04

## Context

Each list that found a game or a mod by its name matched text its own way:

- the Library's filters found the text anywhere in the name, case aside;
- Browse's game box did the same;
- Settings search found the text anywhere in a section's words, case and accents aside;
- the "Also on …" hint (ADR 0063) compared whole names, case and spaces at their ends aside.

None of them found "S.T.A.L.K.E.R. 2" by "stalker 2", "Dragon's Dogma 2" by "dragons dogma", or "Pokémon" by "pokemon".
Quick search (step 15) and the game picker (step 11) will need the same.

The human chose (2026-11-04):

- each word typed starts a word of the name, in any order;
- case, accents, apostrophes, quotes and other punctuation don't count;
- pickers put the names that start with the text first, and lists in an order the user chose keep it;
- Roman numerals from II to XXX are their numbers too;
- the matcher is used by Settings search (which also finds games), Browse's game box and the Library's filters, and by
  a new CLI `mod games` with the MCP server's `mod_games`.

## Decision

- **Application, `Common/`:**
  - `NameQuery.Parse(text)` reads what was typed. It's a record: two queries are equal when they find the same names.
    - `Match(name)` gives a `NameMatch`: `None`, `Words` (each word typed starts a word of the name) or `Start`
      (the name also starts with the text). `Matches(name)` is `Match(name) != None`.
    - `Find(items, nameOf)` gives the items that match, those that `Start` first, each group in the order given.
    - `IsEmpty` is true when nothing that counts was typed; then every name matches.
    - `SameName(a, b)` compares two names with what doesn't count left out, and numerals as numbers.
  - `NameWords` gives a text's words:
    - lowercase, with Unicode compatibility forms (so "ﬁ" is fi, "²" is 2, and full-width letters plain), and accents
      dropped;
    - ß, æ, ø, œ, ł, đ and ı written as ss, ae, o, oe, l, d and i;
    - apostrophes and quotes (' ’ ‘ ‚ ‛ ` ´ " “ ” „ ′ ″ ʼ) left out without ending a word;
    - anything else that isn't a letter or digit ends a word.
  - A typed word matches when the name, from the start of one of its words, starts with it, the name's words run
    together. So "stalker" finds S.T.A.L.K.E.R. (whose words are s, t, a, l, k, e, r) and "spiderman" finds
    Spider-Man.
  - `RomanNumerals` gives each numeral from II to XXX its number, and each number its numeral, as a second form of
    the word. I, V and X alone are left out, as they're as often letters.
- **Presentation:**
  - The Library's game and mod filters match with it (mods by name or author), and keep the chosen order.
  - Browse's game box shows `GameMatches`: the games matching `GameText`, those that start with it first. The box
    filters nothing itself (`FilterMode="None"`).
  - Settings search:
    - sections match with the same rules, against the words they say;
    - the Games section shows too when a listed game's name matches, and the list then holds only those games, the
      first chosen;
    - when only the section's words match, every game is listed.
  - `SameNamedGames.Same` is `NameQuery.SameName`, so "Stardew Valley" and "StardewValley", or "Final Fantasy XII" and
    "Final Fantasy 12", are each one name for the hint.
- **CLI:** `mod games [words] [--source] [--page] [--page-size]` lists a site's games that match, those that start
  with the words first, then by name.
  - It shows each game's key for `mod search --game`, 20 a page (1 to 100).
  - `--source` is needed only when more than one site lists its games.
  - A site that needs an account, or fails, is said as `mod search` says it.
- **MCP:** `mod_games` (`words`, `source`, `page`, `pageSize`) does the same, returning `total`, `offset`, `hasMore`
  and `games`, each with its `source`, `game` (its key), `name` and `page`. The server has 25 tools.

## Consequences

- Text found inside a word no longer matches: "dew" doesn't find Stardew Valley, and "heme" doesn't find the Theme
  setting. Text with nothing that counts, such as "!!!", matches everything.
- Two games are the same name for the hint more often than before, such as "Stardew Valley" and "StardewValley".
  ModShelf still only points the match out.
- Browse's box puts games that start with the text first. Step 11's picker builds on `GameMatches`, and quick search
  (step 15) uses `NameQuery`.

## Testing

- Application: the human's cases as tests:
  - Dragon's Dogma 2, S.T.A.L.K.E.R. 2: Heart of Chornobyl, Final Fantasy XII: The Zodiac Age and Pokémon, each by
    several typings;
  - Spider-Man, the special letters, quotes and full-width text;
  - numerals both ways, single letters left alone;
  - what doesn't match;
  - `Start` and `Words`;
  - an empty query;
  - the order of `Find`;
  - `SameName` and equality.
- Presentation:
  - the Library's filters by the starts of words, in any order, keeping the chosen order;
  - Browse's game box, starting matches first;
  - Settings search finding a game alone, every game for the section's words, nothing, and back on clearing;
  - a section not found inside a word;
  - the hint's same names.
- CLI and MCP:
  - `mod games` listing, finding, the names that start with the words first, nothing named so, paging, its
    refusals, and asking for a site;
  - `mod_games` finding, nothing, every game, a page at a time with the names that start with the words first, and
    its refusals.
- 33 deliberate bugs, 32 caught. Five got through at first, and tests were added for them:
  - accents breaking words, so "mon" found Pokémon;
  - whether a name starts with the text, checked against the first word typed alone;
  - a game's name read after the Settings search was typed;
  - `mod games` not putting the names that start with the words first;
  - `mod_games` never saying there are more.

  The one left can't be caught: every Library order ends by name, so the order the matches come in never shows.
- By hand:
  - `docs/manual-checklist.md` 3.4a;
  - `docs/manual-checklist-desktop.md` 3.1a and 10.0;
  - `docs/manual-checklist-mcp.md` 1.1 and 1.4.
