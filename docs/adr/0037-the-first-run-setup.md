# 37. The first-run setup

- Status: Accepted
- Date: 2026-10-30

## Context

The plan's Phase 6 asks for a first-run wizard: choose the library root, link accounts, and opt in to being the
`nxm://` handler. Each of these has a home already: the library folder and the links on the Settings screen
(ADRs 0028 and 0036), the accounts on the Accounts screen (ADR 0028). Someone new to ModShelf doesn't know to look there
before the first download.

The human approved the shapes below, a setup for new libraries only, and closing it showing it again at the next start
until it's finished or skipped. They chose the last one over the recommendation, which was that closing counts as done.

## Decision

- **A setting says whether the setup is done**: `AppSettings.SetupDone`, kept in `settings.json` as
  `"setup": { "done": true }`, set by `ISettingsService.SetSetupDoneAsync(bool?)`. Null, the default, is not yet. The
  setter takes null, as the other settings' setters do, so that the CLI can set it back: `settings show` lists
  `setup.done`, and `settings set` and `settings reset` change it.
- **Only a new library sees it**: the setup isn't done, the library has no mods, and no account is linked. A library in
  use is marked set up without it, so an upgrade never shows it. When that can't be told, such as with settings that
  can't be read, nothing is shown and the failure is logged; the Settings screen already says what's wrong.
- **Presentation's `FirstRunViewModel`** has three pages: the library folder, the accounts and the links. Each page is
  the screen's own view model, the Settings screen's and the Accounts screen's, so what's chosen there shows on the
  screens at once and stays changeable there. **Back** and **Next** go through the pages; **Finish**, and **Skip setup**
  on any page, mark the setup done and close it. Closing its window otherwise leaves it for the next start. A setup that
  can't be marked done says why, and stays open.
- **`IDialogService.ShowFirstRunAsync`** shows it; Desktop's `FirstRunWindow` sits over the main window once that has
  opened, then the links the app was started with are opened. Dialogs now go over the newest window open, so the API key
  prompt of an account card opens over the setup, rather than behind it.
- **The views it shares** with the screens are views of their own: `LibraryFolderView`, `AccountCard` and
  `LinkSitesView`.
- Events 930–934.

## Consequences

- A test folder made afresh, as both checklists do, starts with the setup; the desktop checklist's first section goes
  through it, finishing it with the Premium account and skipping it with the free one.
- Someone who closes the setup each time sees it at each start until they link an account or download a mod.
- A second source's account card shows on the second page without changes here, as it shows on the Accounts screen.

## Testing

- Application and Infrastructure: the setting set and back, kept in its section, other values there left, and refused
  when it isn't true or false. CLI: `setup.done` shown, set and reset.
- Presentation: a new library is shown the setup over the very screens it changes; closing it shows it again; the
  pages, their steps and buttons; finishing and skipping mark it done, keeping a folder chosen; a library with mods, or
  with an account linked, is marked set up without it; a setup done isn't shown; a failure to tell or to save.
- By hand on Xvfb: the three pages, the API key prompt over the setup, closing and starting again, skipping and starting
  again, and the seeded library marked set up without the setup.
