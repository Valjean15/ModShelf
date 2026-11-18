# 100. Endorse from ModShelf

## Context

Nexus Mods asks its users to endorse the mods they use: endorsements are how authors are thanked and how mods are
found. Until 1.0.8, ModShelf showed a mod's endorsements as a count but gave no way to add yours, so endorsing meant
opening each mod's page on the website. People who keep dozens of mods in the library have to do that one page at a time.

The human approved, for 1.0.8 (feature 3 of the plan):

- A new source capability, `IModEndorsements`, in Sources.Abstractions, with `Endorsement` and two new `SiteWords`.
- A new public interface in Application, `IEndorsements`.

Nexus Mods' v1 API has what's needed, as its own app uses it: `GET v1/user/endorsements.json` lists the account's
endorsements, of every game, each `Endorsed` or `Abstained` (or `Undecided`, which says nothing); and
`POST v1/games/{game}/mods/{id}/endorse.json` and `abstain.json` change one, with the version endorsed in a form. It
refuses some mods, such as one the account didn't download, and says why in its answer.

## Decision

### Sources

- **`IModEndorsements`**, a capability like `IWatchlistSync`: `GetEndorsementsAsync`, the account's endorsements by
  mod, and `EndorseAsync(mod, endorsement, version)`. `Endorsement` is `Endorsed` or `Abstained`; a mod not listed is
  neither. The version is the one the site records; null asks the source to use the mod's own.
- **`SiteWords.Approve` and `Unapprove`**, the site's verbs ("endorse", "abstain" on Nexus Mods), beside `Approval` and
  `Approvals`. `SiteWords.CanApprove` says a site gave both; without them, nothing offers to endorse.
- **Nexus Mods** implements it: the list as above, and endorsing or abstaining with the version given, or the mod's
  current version, read first, without one (one request more). It's the only source that does for now; mod.io's
  ratings could be another, later.
- `SourceContractTests` gains a test that a source with `IModEndorsements` refuses another source's mod, naming
  endorsing, as for following.

### Application

- **`IEndorsements`** (`SiteEndorsements`), as `IWatchlists` is for following: `CanEndorse(source)`, `ListAsync(source)`,
  `EndorseAsync(mod, …)`, and `EndorseAsync(link, …)`, which recognizes the link first and refuses one to a mod of a site
  that can't endorse, as `UnsupportedLinkException`. It holds no state.

### The desktop app

- **A mod's page**, in Browse and the Library, has **Endorse** beside **Rescan**, for a site that can endorse. It reads
  the account's endorsements once, when first needed, then says "Endorsed", with a check; clicked then, it abstains.
  The page says what happened, or why the site refused, in the site's words, as following does.
- **The Library's selection** gains **Endorse (n)**, counting the selected mods not endorsed. It asks first, naming up
  to 10 mods and counting the rest, then endorses one mod at a time, with the site's own version for each; the mods the
  site refused stay selected and are listed once, in its words. There's no **Undo**: abstaining is a different answer,
  which the site keeps and shows, not a way back, so it stays a page's button.
- **A Not endorsed toggle**, shown while a mod of the game isn't endorsed, as the gone toggle is.
- **One list for the app** (`EndorsedMods`, a Presentation singleton, like `ModPictures`), so what's endorsed on one
  screen shows on the other. A site's list is read once a run, when one of its games opens in the Library or a page
  first needs it, and kept up to date with what ModShelf endorses. A list that can't be read, such as without a linked
  account, is logged as a line without a stack trace, and the mods' endorsements stay unknown: no mod is said to be
  not endorsed.

### Command line and MCP

- **`mod endorse <link>`** and **`mod abstain <link>`**, as `mod track` and `mod untrack` are: "Endorsed on Nexus Mods:
  mod 12604 of skyrimspecialedition.", in the site's words. A site's refusal goes to the error output with exit code 1;
  a link to a site that can't endorse, or no mod, exit code 2.
- **`mod_endorse`**, one mod by its link, with `abstain`; its description tells the agent to endorse only the mods the
  user asks to. There's no tool to endorse many mods at once: an endorsement speaks for the user in public, so each one
  is asked for. The server has 29 tools.

### Not done

- **No Undo**, as above, and nothing puts back a mod neither endorsed nor abstained from: the site has no way to.
- `library list` and the MCP server's `library_list` don't say which mods are endorsed: that's a request to the site,
  which listing never makes.
- The endorsements aren't stored: the list is read again each run. Linking another account while the app runs keeps the
  first account's list until the next run.
- Abstaining from many mods at once isn't offered: the Library's button only endorses.

## Consequences

- Endorsing the mods you use takes a selection and one button, on the site that the user already trusts with a linked
  account, and never anything the site's own button wouldn't do.
- One more request when a game of a site that can endorse opens first in a run, and one for each endorsement from a
  mod's page, which knows the version; two for each from the Library's selection, the command line or the MCP server,
  which read the mod's version first.
- New public types, approved: `IModEndorsements`, `Endorsement` and two `SiteWords` properties in Sources.Abstractions;
  `IEndorsements` in Application. No schema change, no new package.
- `BrowseViewModel` and `LibraryViewModel` take the shared `EndorsedMods`.
- Log events 981–983 are new, in Presentation's block.

## Testing

- Nexus Mods, against recorded answers: the list, its statuses, an entry Nexus Mods leaves incomplete and the last of a
  mod listed twice; endorsing and abstaining with the version given, and with the mod's own, read first; a refusal with
  its words. The contract test for every source with the capability.
- Application: the site's list, endorsing a mod and a link, and refusing a site that can't, by reference and by link;
  the registration.
- Presentation: the page's button reading the list once, endorsing and abstaining, a refusal in the site's words, no
  button on a site that can't, a list that can't be read; the Library's button counting, asking with names, endorsing,
  keeping refused mods selected, and the **Not endorsed** toggle.
- Command line and MCP: endorsing and abstaining by link, in the site's words; each refusal, with its exit code; the
  tool's result and its errors; the tool count.
- By hand: the command line's section 19, the desktop app's section 21 and the MCP server's section 10, with a mod the
  account downloaded and one it didn't.
