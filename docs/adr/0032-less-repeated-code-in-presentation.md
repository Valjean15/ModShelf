# 32. Less repeated code in Presentation

- Status: Accepted
- Date: 2026-10-29

## Context

The second review (ADR 0030) read the user interface's code for how it's organized and how much of it repeats. The view models kept to the rules (compiled bindings, `.Log.cs` files, `UserCommands`, a services record per screen), but each screen had grown its own copy of what another screen did too:

- **Formatting:** dates in the user's time zone in six places, one-or-many counts in eleven, a site's name or its ID in four; Browse wrote a mod's details twice.
- **Errors:** five screens decided which errors were the site's, and only the Accounts screen took a request past its client's timeout as one.
- **Commands:** six commands that answer beside their button wrote the same guard; a download row had a word-for-word copy of `UserCommands.RunAsync`; Browse and the Updates screen downloaded into the library with the same twenty lines; the Library, Updates and Browse screens each followed the library the same way; a library mod, an opened mod and a download waiting for its handoff each opened their page the same way.
- **Code-behind:** the text dialog opened its help page from an `async void` handler.
- **Size and place:** five view models had passed 400 lines, and the 57 files of `ViewModels/` sat side by side, helpers next to the screens that used them. `LinkProblems.MessageForUser` worded refusals that had nothing to do with links.
- **Changelogs:** the Updates screen asked the sources itself, around Application (ADR 0031 moved that into `IModLookup`).

The human approved refactoring Presentation as a set of patches of its own, after the core and the CLI's.

## Decision

- **`Common/`** gains `Dates` (a day, a day and time, a time, in the user's zone and culture), `Counts` (one wording for one, another for the rest, and one for none when asked), `SiteNames` (`ISourceRegistry.SiteName`), `SiteProblems`, `Pages` (open a page, or show its address), `LibraryFeed` (watch the library, then list it, posting both to the UI thread) and `ArgumentExceptionExtensions.MessageForUser`. `UserCommands` gains `RunInPlaceAsync`.
- **`SiteProblems`** says what a site's problem is: the account refused, an error answer, or no answer in time, timeouts included. Each screen still words it in its own sentence. A rate limit isn't worded apart: Nexus Mods' says when the limit resets, which "slow down" would lose. The Accounts screen keeps its own words, beside the quota it shows.
- **`RunInPlaceAsync`** runs Check now, adding a link, Clear finished, an account's Check and Unlink, and listing the accounts. Linking an account and moving the library keep their own guard: a closed dialog cancels a link without the app closing, and a move that's cancelled or not confirmed skips what follows it.
- **`DownloadNotes`** (Downloads) adds a link for the library from another screen and says what became of a link added for a download. "{0} is in the library already." is one resource, not two.
- **`ModInfoText`** (Browse) writes a mod's details for a search result and an opened mod.
- **`TextQuestionViewModel`** (Dialogs) holds a text question, its answer and its help page, tested without the UI. The dialog binds to it, hides a secret with a style class, and keeps only focus and its buttons in code-behind.
- **The Updates screen** reads an update's changes through `IModLookup.GetChangesAsync`; its own `Changelogs` cache is gone. The settings screen compares folders with `FilePaths.Same`.
- **Partial files:** `BrowseViewModel.Games` and `.Listing`, `SettingsViewModel.Root` and `.Saving`, `AccountViewModel.Linking`. The download row and the opened mod fell below 400 lines with the changes above.
- **Feature folders:** `ViewModels/Accounts`, `Browse`, `Downloads`, `Library`, `Settings`, `Updates` and `Dialogs`, each its own namespace; the main window's and the tray's view models stay at the root. The tests move to the same folders. `docs/code-organization.md` says so.
- **Not done:** a `Note` record for the eight message-and-flag pairs. It would change the public properties the views and about 30 test assertions bind to, and nothing a user sees; the notes' shared look comes with the desktop's styles instead.

## Consequences

- **No text changes.** A source whose HTTP client times out is told like any other failure of its site, on every screen; Nexus Mods reports its timeouts as `HttpRequestException` already.
- **The desktop app** names a namespace per screen in its views, and `App.axaml` one per screen's template.
- **The text dialog** hides an API key through the `secret` style class rather than a character the dialog service picked.

## Testing

- Every test project, before and after each commit. The Presentation tests go from 376 to 418: `Counts`, `Dates`, `SiteProblems`, `DownloadNotes`, `RunInPlaceAsync` and `TextQuestionViewModel` are new; the rest pass unchanged, moved into the feature folders.
- The text dialog is covered by the desktop checklist's 2.2 (the key is hidden) and 2.3 (the API key page opens).
