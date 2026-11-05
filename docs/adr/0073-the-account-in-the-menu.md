# 73. The account in the menu

- Status: Accepted
- Date: 2026-11-05

## Context

The sites give each account's picture and level: Nexus Mods its avatar and "Premium", "Supporter" or "Free". The
database kept them (`LinkedAccounts.AvatarUrl` and `Tier`), and no screen showed the picture. The Accounts screen showed
the account's name, level and the day it was linked, and listed the accounts only once, as the app started.

The approved mockups (step 8) put the account at the bottom of the menu: its picture, its name, and its level and site;
its initial on a colour without a picture; the picture alone with the menu collapsed; one row per account with two
sites; and Link an account without one.

The human chose (2026-11-05):

- with no account linked, the menu offers **Link an account**;
- **Accounts stays** in the menu's list of screens, beside the account at the bottom;
- the second line **always names the site**, "Premium · Nexus Mods", even with one site in use: an account belongs to a
  site.

## Decision

### The menu

- `AccountMenuViewModel`, in Presentation's `ViewModels/Accounts/`, a singleton the main window shows
  (`MainWindowViewModel.Accounts`, a new constructor parameter):
  - `Entries`, one `AccountMenuEntryViewModel` per account linked, in the order of the sites;
  - `HasAccounts`, and `OffersLinking` once the sites are known and none has an account;
  - `OpenAccountsCommand`, which every row and Link an account run.
- `AccountMenuEntryViewModel`: `Picture` (the account's picture, read through the saved pictures as a game's cover is),
  `Name`, `Level` ("Premium · Nexus Mods", or the site's name alone without a level), `Initial`, `Hue` (from the name,
  as the games' covers' are from their keys) and `Tip` ("Kestrel · Premium · Nexus Mods").
- The menu shows what the Accounts screen shows: each card tells it of its account (`Show(source, siteName, account)`)
  whenever the card shows one, as the screen lists the accounts, links one, checks it or unlinks it. Before the screen
  has listed them, the menu shows nothing, so Link an account doesn't flash at start.

### The Accounts screen

- Each card shows the account's picture, or its initial on its colour, beside its name (`AccountViewModel.Picture`,
  `Initial`, `Hue`).
- Each time the screen is shown, it lists the accounts again, so an account linked or unlinked from the command line
  meanwhile shows, on the cards and in the menu, and the API requests left are as last said. A card busy linking,
  checking or unlinking is left as it is. A listing that fails is logged (event 920, as at start), and the cards stay.
- `AccountsViewModel` takes `ModPictures` and `AccountMenuViewModel`, and hands them to the cards with the other services.

### Desktop

- `MainWindow`: at the bottom of the sidebar, in a quiet box (`Border.accountMenu`), a `Button.accountRow` per account,
  with its round picture (`Border.avatar`, 32 pixels), name and level; or Link an account, with a plus in a ring
  (`PlusIcon`) and "To download mods". Collapsed (`.collapsed`), the box goes, and the pictures show alone, each named in
  its tooltip.
- `AccountCard`: the picture, 40 pixels, before the name.

## Consequences

- No database, settings or Application change: the picture and level were kept already.
- A picture is read once, then kept with the other saved pictures; offline, the saved one shows, and before it's saved, the
  initial.
- An account changed from the command line shows once Accounts is shown, not at once.
- The Accounts screen asks the database again each time it's shown, which is cheap; it still asks the sites nothing.

## Testing

- The menu: each account in the sites' order with its level and site, or the site alone; Link an account with none, with
  one site or two; nothing before the sites are listed; the picture read for the menu and the card, and the initial while it's being read
  or when it can't be; following the cards as they link, check and unlink; the screen listing them again when shown, a
  busy card left alone, a failure logged, nothing listed before the screen has started; opening Accounts.
- The main window: its accounts open Accounts.
- 26 deliberate bugs, all caught. One got through at first, Link an account offered only with two sites, as the tests
  all had two: a test now has one.
- Looked at here, on Linux, with an account made up in the scratch database (no credentials): Link an account, then,
  once Accounts was shown, the account's row and the card's initial; the sidebar collapsed to the initial alone; the row
  opening Accounts from the Library. No real picture was read.
- By hand: `docs/manual-checklist-desktop.md` 2.1, 2.4, new 2.6, and 14.1.
