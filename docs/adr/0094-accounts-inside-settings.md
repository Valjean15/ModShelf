# 94. Accounts inside Settings, and Settings at the bottom of the sidebar

- Status: Accepted
- Date: 2026-11-11

## Context

The sidebar listed six screens, Library, Updates, Browse, Downloads, Accounts and Settings, with a block of the linked
accounts at its bottom (ADR 0073), and the Accounts screen held a large card per site that can link an account. With
four sites, two of them needing no account, the human asked (2026-11-11, plan 1.0.5, step 20) to reorganize accounts,
and to replace the sidebar's account block with access to settings or something more useful. They chose:

- **Accounts inside Settings**: the sidebar keeps Library, Updates, Browse and Downloads, with a Settings button at its
  bottom carrying a badge when an account needs attention.
- **Compact rows grouped by need**: needs an account, optional, no account needed; a row opens for its details.

## Decision

### The sidebar

- **`MainWindowViewModel.SidebarScreens`**: Library, Updates, Browse and Downloads. **`SidebarScreen`** is the one shown,
  or none while Settings is, so the list selects nothing then; setting it goes to it.
- **Settings at the bottom** (`SettingsScreen`, `OpenSettingsCommand`), styled as a screen of the list, raised while
  Settings shows (`IsSettingsShown`); collapsed, its icon alone.
- **Its badge** (`SettingsBadge`) adds Settings' own (a newer ModShelf to install, ADR 0079) and Accounts' (below); its
  tooltip says "Settings: 2 to look at".
- The account block goes, with `AccountMenuViewModel`, its entries, their strings and their tests.

### Accounts, a category of Settings

- **`SettingsCategory.Accounts`**, first; Settings opens on it. `SettingsViewModel.Accounts` is the accounts' view model,
  shown when the category is (`AccountsSection`, which Settings' search also finds by "account", "API key" and such).
  Choosing the category, or showing Settings on it, lists the accounts again, as showing the Accounts screen did.
- **`Screen.Accounts` stays the way in**: navigating to it shows Settings (`MainWindowViewModel` shows Settings for it,
  and leaves the navigation on Accounts), and Settings, told of it, chooses the category. Browse's **Go to Accounts**,
  Downloads, quick search and `modshelf://accounts` keep working unchanged.
- The strings that sent the user to "the Accounts screen" now say "Settings › Accounts".

### The rows

- **Grouped** (`AccountsViewModel.NeedOne`, `Optional`, `NoneNeeded`): sites whose source links accounts by
  `IAccountLinker.WorksWithoutAccount`; then the sources that don't link accounts at all (DEG Mods, direct links), each
  saying what ModShelf does there, from the capabilities it has: "ModShelf searches its mods, downloads its files and
  checks them for updates, with no account." No interface changed.
- **A row** (`AccountRow`): the account's picture or an empty ring, the site and the account's level, the account's name
  with what's left of the requests that run out soonest (`RequestsText`), and **Link…** (opening it) or **Check**;
  **Details** opens the rest, as the card held it: linked on, the requests, the ways to link, **Unlink**. The first-run
  setup keeps the cards (`AccountCard`).
- **Needs attention** (`AttentionText`, counted by the screen's badge): downloads of the site failed for want of an
  account (`SitesWantingAccounts`, which the Downloads screen keeps as it counts its badge), or the site refused the
  account when checked. Checking or linking it again, or unlinking it, clears the refusal; retrying or clearing the
  downloads clears the other.
- **Not counted**: a site's requests running low, which the strip at the top of the window says already (ADR 0077); the
  plan had it in the badge too.

## Consequences

- One click less to Settings, and none more to accounts, which open with it; the sidebar's bottom no longer grows with
  each site's account.
- The Accounts screen's view model stays a screen, for the way in and the first-run setup, but shows only inside
  Settings.
- Settings › Accounts depends on Settings having loaded, as its other categories do.

## Testing

- Presentation: the sidebar's screens and selection, Settings' button and its badge with its tooltip, Accounts shown in
  Settings with the navigation left on it; Settings' categories with Accounts first and its search; the accounts listed
  again each time they're shown; the groups and the sites with no account; a row's summary, Link… and Details; attention
  from downloads and from a refused check, and what clears it; the Downloads screen saying which sites' downloads failed
  for want of an account; registration.
