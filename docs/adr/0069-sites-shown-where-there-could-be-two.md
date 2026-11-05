# 69. Sites shown where there could be two

- Status: Accepted
- Date: 2026-11-05

## Context

The screens never said which site a game, a mod, an update or a download was from. With Nexus Mods alone, that was
right: saying "Nexus Mods" on everything tells the user nothing. Once a second site comes, in 1.0.1, the same game can
be on both (ADR 0063), and the Library, Updates and Settings would list two "Stardew Valley" side by side.

ADR 0063 named the same game on two sites with its site's, as in "Stardew Valley (Nexus Mods)", in Settings and Browse,
and said "Also on Example Mods" in the Library. That covers two games of the same name, not a mod or an update, nor
which site a game's page is about.

The human chose (2026-11-05):

- sites shown once **two sites are in use**: those the library has mods of, and those with an account linked;
- a chip **everywhere a game shows**: the Library's games, the open game and its activity, Updates' games, Downloads'
  rows, Settings' games and a game's page there, and a mod's page;
- the Library's site filter **beside the game order**, not remembered.

## Decision

### When sites show

- **`ISitesInUse`**, in Presentation's `Services/`, says whether two sites or more are in use: the sites of the library's
  mods, then, while that's fewer than two, those with an account linked. `SitesInUse` reads `IModLibrary` and
  `IAccountService`, and `AddPresentation` registers it.
- **Direct links aren't a site:** the library never holds their files, and they have no account.
- **When it can't tell**, because the library or the accounts can't be read, it says no and logs a warning (event 950):
  a site shown too seldom misleads less than one shown wrongly. Stopping isn't taken for a problem.
- **Each screen asks when it's shown**, since an account linked on the Accounts screen, or a mod of another site
  downloaded, changes the answer while another screen shows. Browse asks when it opens a mod's page.

### The chip

- A small outlined label with the site's name, such as "Nexus Mods", from the source's display name: `Border.site` in
  `Shell.axaml`, beside the name.
- **Where:**
  - the Library: each game, as a card or a row; the open game's heading; each entry of the recent activity; and a mod's
    page opened from it;
  - Updates: each game's heading;
  - Downloads: each download from a mod site, never a direct link's;
  - Settings: each game in **Games**, and the chosen game's page;
  - Browse: a mod's page. The game box already says "Stardew Valley (Example Mods)" when two sites can search (ADR
    0063); its chips come with the game picker, step 11.
- **Each view model** has `SiteName` and `ShowSite`; a screen with chips has `ShowSites`, and sets each one's `ShowSite`
  as it changes. Games, headings and rows made afterwards take it too.
- **Settings' names:** while chips show, the same game on two sites goes by its own name, "Stardew Valley", beside its
  chip; without them, by "Stardew Valley (Nexus Mods)" as before. **Copy settings from …** still names the other game
  with its site, since that's a button, not a game.
- **"Also on …"** stays: it says where else the game is, which the chip doesn't.

### The Library's site filter

- **A Site box** beside the game order, while sites show and the library's games are of two sites or more: **All
  sites**, then each site by name.
- **A site chosen** shows its games only, and the summary counts its games, mods and room on disk only. The game filter,
  the order and **Show hidden games** work within it.
- **Not remembered:** the screen starts on **All sites**. It goes back to **All sites** when the chosen site's last game
  leaves, when sites stop showing, or when no site is chosen, as the box does when the site it shows goes.

## Consequences

- With Nexus Mods alone, as in 1.0.0, nothing shows. The first second site, in 1.0.1, brings the chips and the filter
  with no change outside its own project.
- Showing a screen reads the library's mods once more, and the accounts when the mods are of one site: from the
  database and the secret store, never from a site.
- `GameSettingsViewModel`, `GameRowViewModel`, `LibraryGameViewModel`, `UpdateGameViewModel`, `ActivityEntryViewModel`
  and `ModPageViewModel` take their site's name; `DownloadJobViewModel` knows whether its file is from a mod site.
- The Library's `SelectedSite` can be null, as a combo box sets it, and is then all sites again.

## Testing

- `SitesInUse`: no site, one site's mods, two sites' mods without asking the accounts, an account on another site, two
  accounts and no mods, an account not linked, an account on the mods' own site, a library or accounts that can't be
  read, and stopping.
- The Library: no site with one; each game's and the open game's chip; a game added later; asking each time shown and
  sites going; the filter's choices by name; a site's games and counts; no site chosen; the game filter within a site;
  no filter with one site's games; the chosen site's last mod going; the activity's chips coming and going; a mod's page.
- Updates: no chip with one site; each heading's; a heading added later; sites going.
- Downloads: no chip with one site; a mod site's download but not a direct link's; downloads added later; sites going.
- Settings: names with sites and no chips with one site; chips and plain names with two; the game's page and its copy
  button; sites going.
- Browse: a mod's page with one site and with two.
- 52 deliberate bugs, all caught. Two didn't build, for the compiler's and the analyzers' checks, and were rewritten.
- By hand: `docs/manual-checklist-desktop.md` 9.2b.
