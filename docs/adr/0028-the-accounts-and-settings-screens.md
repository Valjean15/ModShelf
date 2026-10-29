# 28. The Accounts and Settings screens

- Status: Accepted
- Date: 2026-10-29

## Context

Step 5 of Phase 5 (ADR 0023) fills in the last two screens. The plan gives them:

- **Accounts:** link and unlink per source, see the tier and the API quota.
- **Settings:** the library root, path templates, the check interval, notifications and the theme.

Three things were missing below the screens:

- **The quota.** The Nexus source reads the rate-limit headers of every answer into its `NexusQuota` (ADR 0012), which was internal until something needed to show it.
- **The tier as it is now.** An account's tier was read once, when it was linked. A user who upgrades keeps seeing the old one.
- **The theme.** No setting kept it; the app always followed the system.

The human chose, before anything was built:

- **The API, as proposed:** a quota capability in Sources.Abstractions, which the Nexus source fills from its headers; `IAccountService.CheckAsync` and `SourceAccountStatus.Quota` in Application; an `AppTheme` setting; and `account check` and `appearance.theme` in the CLI.
- **Accounts: the sites are asked only when the user asks.** Each site's card shows the account's name, tier, linked date and quota; the tier and quota change only when the user links or checks.
- **Settings: everything as listed,** including moving the library's files when its folder changes, per-game patterns, and a warning when no keyring protects the credentials.

## Decision

### The quota

- **`IApiQuota`** (Sources.Abstractions, `Quota/`) is a capability a source implements when its API has allowances: `ApiQuota? Quota`, null before the site has answered.
- **`ApiQuota`** lists one or more **`QuotaAllowance`s**, the longest first: how long it lasts, its limit, what's left, and when it starts over.
- **The Nexus source** reports its daily and hourly allowances from the last answer's headers. A count below zero reads as none.
- **The shared contract tests** check that a source with a quota knows none before the site has answered.

### Checking an account

- **`IAccountService.CheckAsync(source)`** asks the site who the linked account is now, with its stored credentials, and keeps the profile it gets, so a changed tier shows. Credentials about to expire are refreshed first, as for any other request. The account keeps its linked date.
  - No account, or a source that can't link one: `KeyNotFoundException`.
  - Credentials the site refuses, or that can't be used any more: `AuthenticationFailedException`, and the account stays as it was.
- **`SourceAccountStatus.Quota`** is the source's quota while an account is linked there; null otherwise.
- **`account check <source>`** prints the account and a table of each allowance: what's left, of how much, and when it starts over, in local time. Without a quota it says the site doesn't say. No linked account, a refusal, a site that's busy, failing or unreachable, and a keyring that fails each say what to do, with exit code 1; an unknown source is exit code 2.
- **Log event 409:** an account was checked.

### The theme setting

- **`AppTheme`**: System, Light or Dark. **`AppSettings.Theme`** is null for System, and **`ISettingsService.SetThemeAsync`** sets it.
- **`settings.json`** keeps it as `"appearance": { "theme": "dark" }`, read in any case. Other settings in `appearance` stay.
- **`settings show`, `set` and `reset`** know `appearance.theme`.

### The Accounts screen

- **A card per site** that can link an account: its name, whether it needs one, the account's name, tier and linked date, and what's left of the API requests, as the site last said. Listing the cards asks no site anything.
- **A button for each way the site offers to link,** the first one the main button while no account is linked. Linking again replaces the account.
  - **An API key** is pasted into a dialog that hides it. A button opens the site's API key page; when the browser doesn't open, the dialog shows the page's address.
  - **An emailed code** asks for the address, then the code.
  - **In the browser:** the card says to approve ModShelf in the page that opened, or shows the page's address when the browser didn't open, with **Open page again** and **Cancel**.
  - Closing a dialog or cancelling links nothing and says nothing.
- **Check** asks the site again, and shows the tier and the quota it gives. **Unlink** asks first.
- **Each problem says what to do:** a refused key, code or approval; a site that asked to slow down, answered with an error, or couldn't be reached; a blank answer; and a keyring that fails. Anything else is logged and says something went wrong.
- **The screen warns** when no keyring protects the credentials, as the CLI's account commands do.
- **`TextQuestion`** gains `IsSecret` and a help page with its button's label, which the text dialog shows. The dialog opens the page through `IBrowserLauncher`.

### The Settings screen

- **Library folder:** **Choose another folder…** and **Use the default folder**. An empty library changes folder at once. One with files asks first, then moves them with `ILibraryMover`, and says if any weren't on disk. Whatever happens, the folder shown is the one stored, and a refusal, such as unfinished downloads or a file that couldn't move, says why.
- **Folder patterns,** for every game and for single games: checked as they're typed, showing where a sample file would go under the library's folder, or what's wrong, and saved with **Save**. Every game's has **Use the default**; a game's has **Remove**. **Add a game…** asks for the game, as its site names it in links, and starts it with every game's pattern. An edit in progress survives other changes.
- **Old versions** (keep every version, move them to `_archive`, or keep the newest N), **how often to check for updates**, **whether to show a notice** and **the theme** are saved as they're chosen. A choice back at its default leaves the settings file.
- **Changes go one at a time, in the order made,** and each one shows the settings as stored after it. A choice that can't be saved says why, and the screen shows what's stored. Settings that can't be read say so, and can't be changed until they can.
- **The theme** is applied as it's chosen, and at start, when the screen reads the settings. **`IThemeSwitcher`** (Presentation) does it; Desktop sets Avalonia's theme variant, `Default` for System.
- **Where the settings are kept,** and the keyring warning.

### Logging

| ID | Event |
|---|---|
| 409 | An account was checked |
| 916 | Linking an account failed unexpectedly |
| 917 | Checking an account failed unexpectedly |
| 918 | Unlinking an account failed unexpectedly |
| 919 | An account couldn't be listed again after a change |
| 920 | The linked accounts couldn't be listed |
| 921 | Changing the settings failed unexpectedly |
| 922 | Changing the library's folder failed unexpectedly |

### Testing

- **Sources.Abstractions:** the quota's rules. **Nexus:** the quota from the recorded headers, and counts below zero.
- **Application:** checking an account with its stored, refreshed or unusable credentials, a site that refuses, no account or source, and the quota listed only for a linked account.
- **Infrastructure:** the theme in `settings.json`, read in any case, a wrong one, and other appearance settings kept.
- **CLI:** `account check` against the fake site and the fake Nexus API's counting rate-limit headers, with each problem; `appearance.theme`.
- **Presentation:** each card's flows and problems, the browser step on the UI thread and late, and each setting saved, refused, in order and back at its default.
- **By hand,** on a virtual display: both screens in light and dark, the API key dialog with the browser opening and not, the theme switching at once and at the next start, and the settings file after each choice.

## Consequences

- **The quota is what the site last said while the app runs.** After a restart, the Accounts screen shows none until the site answers a request, such as a check.
- **A linked account's tier changes only when the user checks it.** Linking again also reads it.
- **`TextQuestion` has more to it,** for any dialog that asks a secret or points to a page.
- **The settings file can hold `appearance`.** An older ModShelf ignores it.

## Open

- **The Accounts screen lists the accounts at start.** An account unlinked from the CLI while the app is open shows as unlinked once the user checks it; one linked there shows after a restart.
- **A combo box with the focus changes with the mouse wheel,** as Avalonia's do, so scrolling the Settings screen over the update interval right after choosing one changes it.
