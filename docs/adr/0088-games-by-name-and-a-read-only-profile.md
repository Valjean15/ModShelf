# 88. Games by name, each site apart, and a read-only GameBanana profile

- Status: Accepted
- Date: 2026-11-10

## Context

ADR 0087 left one thing open before 1.0.2's release: GameBanana's list of games is 97 requests, which took 57 seconds
the first time in a run, and Browse, quick search and Add a game read every site's games before showing any, so Nexus
Mods' waited for GameBanana's. Trying the release, the human also asked (2026-11-10) whether a user could link their
GameBanana account.

What GameBanana's API offers (asked 2026-11-10, then recorded with `Fixtures/Record-GameBananaFixtures.ps1`):

- **`Util/Game/NameMatch?_sName=<word>`** finds games whose names hold the word, 50 a page at most, with each game's
  banner and icon (`…/_.gif` when it has none), in 0.2 to 0.4 seconds. The word is matched as typed, a phrase in its
  order, with no Roman numerals; under 3 letters it's refused (400).
- **No way for an app to sign a member in.** `apiv11` has no sign-in of its own, and the legacy app API
  (`api.gamebanana.com/Core/App/Authenticate`) wants an app GameBanana registered and the member's app password, which
  ModShelf would have to ask for and keep.
- **What anyone can see of a member:** the member search (`Util/Search/Results?_sModelName=Member`), their profile page
  (`Member/<id>/ProfilePage`: the name, the picture, `_bIsPrivate`; a banned member's tells only of the ban), and their
  subscriptions (`Member/<id>/Subscriptions?_nPage=<n>`, 15 a page, whatever is asked: mods, works in progress and
  more, each mod with its game). A member that doesn't exist is a 404 `NO_SUCH_RECORD`, and `Member/linked/…` no
  route, a 404 `NO_SUCH_ROUTE`.

The human chose (2026-11-10):

- **Games:** search GameBanana's by name, and read each site's games apart.
- **Accounts:** link a GameBanana profile by its name or link, read only.
- **Subscriptions:** list them, as Nexus Mods' tracked mods are listed, and add **Watch all**, which watches them in the
  library.

## Decision

### Games by name

- **`IGameSearch`**, a capability of its own in `Sources.Abstractions`: `FindGamesAsync(words)` finds the site's games
  by name, for a site with too many to list them all; the app narrows what it finds with its own matching. A source has
  it only when its site has such a search.
- **GameBanana** asks `NameMatch` for the longest word typed of 3 letters or more, lowercase, two pages of 50 at most,
  and keeps each word's answer for the run, for the first 200 words asked. Shorter words are refused, saying a word needs 3
  letters. A game found has its banner and icon, none for the blank one.
- **`IModLookup.FindsGamesByName(source)`** and **`FindGamesAsync(source, query)`**: for a site that finds its games by
  name, its own search, asked each time, and nothing typed refused, saying the site has too many games to list them
  all; for the others, the whole list, read and kept as before. `mod games` and `mod_games` go through it, so GameBanana
  needs words there; the app never reads its whole list.
- **The screens** (`GamesByName`): the game picker, quick search and Settings' Add a game ask a site that finds its
  games by name as the name is typed, each answer taking the earlier one's place, saying "Finding games on GameBanana…"
  meanwhile, and that nothing was found only once the site has answered. The picker's hint says which sites list their
  games and which find them by name. A game found can be chosen, and stays offered.
- **Browse** reads every site's games at once, and shows each site's as soon as it answers, so a slow site keeps none
  of the others waiting; of a site that finds its games by name, it reads only the library's games, named by the site,
  so **Your games** and the game Browse starts on include them. Event IDs 966 and 967 log a site whose search refused
  or failed.

### A GameBanana profile, read only

- **`AuthKind.PublicProfile`** (4): the user names their public profile on the site, by its name or its link. Nothing
  secret is asked, and only what anyone can see is read. `IAuthPrompt.AskProfileAsync` asks for it; the desktop app's
  Accounts screen offers **Link your profile**, and `account link <source> --with profile` asks in the terminal, each
  saying ModShelf reads only what anyone can see, and asks for no password. A refused one says the site "has no such
  member, or doesn't show their profile".
- **`GameBananaProfileStrategy`**: a profile link (`gamebanana.com/members/<id>`, with or without `https://` and
  `www.`), else the member of that name as the member search finds them, in any case, else a number as a member's ID.
  The credentials are the member's ID. They're kept in the secret store as any credentials are, though nothing in them
  is secret. `Authorize` puts the ID where a request names the linked member, `apiv11/Member/linked/…`, and leaves every
  other request as it is; unlinked, that path is GameBanana's 404, which says to link a profile. Nothing refreshes.
- **`GameBananaSource : IAccountLinker`**, working without an account. `GetProfileAsync` reads the profile page for the
  name and picture, without a level, and refuses a member GameBanana doesn't have, doesn't show (banned) or keeps
  private.

### Followed mods ModShelf can only read

- **`IWatchlistReader`**: the mods the account follows, to read; **`IWatchlistSync`** is now the reader ModShelf can
  change too. A reader's words name `Followed` alone (the contract checks it); GameBanana's is "subscribed".
- **GameBanana's subscriptions:** the linked member's, 15 a page, every page at once after the first says how many,
  40 pages at most (600 subscriptions); mods only, each once, works in progress and the rest left out.
- **`IWatchlists.CanChange(source)`**: false for a reader. Following or no longer following one of its mods is refused,
  saying "ModShelf can only read your subscribed mods on GameBanana: change them on its website."; the mod page has no
  button for it; `mod tracked` ends with where to change them, without `mod untrack`.
- **Watch all (N)**, beside Browse's followed mods of a game, on every site: watches in the library each mod listed that
  it doesn't watch yet, one after the other, after the API quota check, then says how many it watched and why any
  couldn't be (event 968 for what it didn't expect).

## Consequences

- Browse opens on Nexus Mods' games as quickly as before GameBanana; GameBanana's games come as their names are typed,
  in a fraction of a second each, and the app never reads its 97 pages of games.
- `mod games` and `mod_games` without words list Nexus Mods' games, and are refused for GameBanana. A GameBanana game is
  found by a word of 3 letters or more: "p5" finds none of GameBanana's, "persona 5" all of Persona 5's, and Roman
  numerals only as GameBanana's names have them.
- A GameBanana profile can be anyone's: linking one proves nothing, and only shows what its subscriptions are. A private
  profile can't be linked, and one made private after linking is refused when the account is checked again.
- Changing a subscription stays on GameBanana's website; a member's subscriptions to anything but mods aren't shown.
- A source can add a sign-in that reads only, through `PublicProfile` and `IWatchlistReader`, without changes outside
  its project (`docs/adding-a-source.md`).

## Testing

- 51 recorded answers: the name search, a member found by name, their profile and subscriptions, a member that doesn't
  exist, and the linked path unlinked.
- Abstractions and Application: `ModLookup`'s two ways of finding games and its refusals; the profile link and its
  refusals in `AccountService`; read-only watchlists in `Watchlists`; the contract's words for readers, and a check that
  a site finding games by name finds the searchable game by the longest word of its name.
- Presentation: the picker, quick search, Browse and Add a game with a site that finds its games by name, stale answers
  dropped; each site's games as it answers; the profile link on the Accounts screen; Watch all, its count, its quota
  check and its failures.
- The CLI and the MCP server: `mod games` both ways, `account link --with profile`, `mod tracked`, `mod track` refused
  for a reader, `accounts_status`.
- GameBanana: the longest word, pages and answers kept; profile links and names, refusals, private and banned members;
  `Authorize` on relative and absolute addresses, never past the path; the subscriptions' pages, their cap and what
  isn't a mod.
- Tried from the command line against GameBanana itself (2026-11-10): `mod games persona 5`, without words and with
  `p5`; `account link gamebanana` by name, by link, and with names and links it refuses; `mod tracked`, `mod track`
  refused, and `account unlink`.
- 83 deliberate bugs, 82 caught. The one left drops Watch all's own note that a mod is now watched, which the library's
  change, coming to Browse as any change does, notes too. Survivors on the first pass led to tests for the answers kept,
  a game without a name, a padded profile link, a site that finds games by name but can't describe them, a link to a
  mod whose site ModShelf can only read, a game listed and found by name, Watch all's quota check, its note for one mod
  and its refusals, and `accounts_status`'s `profile`.
