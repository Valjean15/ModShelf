# 33. Less repeated markup and strings in the desktop app

- Status: Accepted
- Date: 2026-10-30

## Context

The second review (ADR 0030) also read the desktop app's markup and the strings its screens show. The views kept to compiled bindings and shared styles in `Shell.axaml`, but repeated what those styles could hold:

- **Messages:** eight notes that say what a command did, sometimes a problem, wrote the same five lines: the note class, the problem class, wrapping, the text, and hiding it while it's empty.
- **Empty screens:** four screens centred the words for an empty list the same way.
- **Bullets:** the Updates screen and an opened mod drew a version's changes with the same template.
- **Dialogs:** the message, choice and text dialogs set the same window properties, margin and row of buttons.
- **Small copies:** tags repeated the alignment their style sets; the number of versions to keep stopped at 99, a copy of `OldVersionRule.MaxCount`; the theme switcher had its own copy of `AvaloniaUiThread`; the tray's tooltip was copied from its view model by hand; `AppTray`, which decides how the app ends, sat among the Avalonia services.
- **Strings:** strings that several screens use kept the name of the first screen that had them (Browse showed an author with `LibraryByAuthor`), and two notes were glued from two strings in code, so a translation couldn't change their order.

The human approved refactoring the desktop markup and strings as the last set of patches of the review.

## Decision

- **`Shell.axaml`** gains the classes `message` (wrapped, shown only while it says something), `empty` (centred), `bullets` (an items control of lines, each after a bullet), and `dialog` and `dialogButtons` for the dialogs, which keep their own widths.
- **The count of versions to keep** stops at `SettingsViewModel.MaxKeepCount`, which is `OldVersionRule.MaxCount`.
- **`AvaloniaThemeSwitcher`** posts through `IUiThread`. **`AppTray`** moves to `Hosting/`, beside the host and its lifetime, and binds the icon's tooltip.
- **Strings shared by screens are named for what they say:** `ModByAuthor`, `ModOpenPage`, `ModWatch`, `ModUnwatch`, `FileVersion`, `FileUploaded`, `FileDownload`, `SaveToFolder`; what became of an added link, `AddedDownloadingInto`, `AddedDownloadingAlready`, `AddedInLibraryAlready`, `AddedSavedAlready`, `AddedDownloadingFileInto`, `AddedContinuing`; `SiteNeedsAccount`, `CredentialsNotProtected`, `BrowserDidntOpenTitle`, `OpenPageByHand`, `OpenHandoffPageByHand`.
- **A sentence is one string:** a check's summary takes what it found as a placeholder (`CheckSummary`), and the library's new folder with the files missing from it is `SettingsLibraryIsInMissing`.
- **Kept apart:** strings with the same words used in other places, such as "Cancel" for a dialog, a download and linking, or "Updates" as a screen's title and a settings heading. Other languages may word them differently.

## Consequences

- No text, layout or behaviour changes.
- A string's name says where it may be used: `Mod…` and `File…` anywhere a mod or a file is shown, `Added…` wherever a link is added, a screen's prefix on that screen only.

## Testing

- Every test project, before and after each commit.
- **The screens, captured before and after** on a virtual display over the same seeded library and downloads, with a keyring on a private session bus: the Library (with a mod opened), Updates (a changelog's note, a check with failures), Browse (a refusal), Downloads (the cancel dialog), Accounts (the API key dialog) and Settings. No pixel differs but the text cursor's and the pointer's. The bullets, rendered with the old template and the new class side by side, look the same.
- **By hand:** under a stand-in tray host, the icon's title is "ModShelf: 3 updates" for three updates, and closing the window asks the same question in the same dialog as before; Dark turns the window dark at once; the count of versions stops at 99.
