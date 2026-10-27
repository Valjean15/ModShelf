# 22. Notices of new updates

- Status: Accepted
- Date: 2026-10-27

## Context

Step 5 of Phase 4 (ADR 0018) finishes the phase. The plan says:

- `INotificationService` raises an OS notification when something new appears.
- **Done when:** a library of 200 watched mods across 5 games completes an update check within Nexus quota, and moving the library root leaves every record valid.

The human chose:

- **How:** each system's own way, with no new package. Windows shows a toast, macOS goes through `osascript`, and Linux through libnotify.
- **What raises a notice:** only new updates that a background check finds. `library check` in the CLI keeps printing them.
- **The shapes:** as proposed before building, and recorded below. The setting is `updates.notify`, beside `updates.interval`.
- **The quota test:** the CLI with the real Nexus Mods source, against an API served by WireMock.Net.

A NuGet package was looked at and turned down. DesktopNotifications was archived in January 2026, its forks have about a thousand downloads, and none of them supports macOS.

## Decision

### The port

**`INotificationService.ShowAsync(Notification)`** (Application/Notifications/Ports) shows a notice the way the system shows them. Platform implements it for each system.

- **`Notification(Title, Message)`** is the notice: its first line, and what it says under it.
- **A notice that can't be shown is logged, never thrown.** Nobody asked for a notice, so a failed one never stops what was asked for.

### Notices of new updates

**`UpdateCheckService`** shows a notice after a background check, when both of these hold:

- the check found updates that no earlier check had found (`UpdateCheck.Found`);
- `updates.notify` isn't false.

The notice is one per check, however many updates it found:

- **The title says how many updates there are:** "1 new update", or "3 new updates".
- **The message names the mods:**
  - one or two by name, such as "SkyUI and SMAPI";
  - for more, the first two and how many others, such as "SkyUI, SMAPI and 1 more".
- The names come from the library. A mod the library doesn't have goes by its ID.

What happens when something goes wrong:

- **Settings that can't be read** mean a notice, as the default.
- **A notice that can't be made or shown** is logged (event 705), and the checks go on.

**The text** is in Application's new `Resources/Strings.resx`. It's set up as in Platform and Presentation, with an internal `Strings` class generated at build time.

**`AddBackgroundUpdateChecks()`** now also needs Platform's `AddPlatformServices()`, which registers the notices.

### The setting

**`updates.notify`** is true or false, true by default.

- `AppSettings.NotifyOfUpdates` keeps it, and null means true.
- `ISettingsService.SetNotifyOfUpdatesAsync` changes it.
- `settings.json` stores it as `"updates": { "notify": false }`. Any value but true or false is refused when the file is read.
- `settings show`, `settings set` and `settings reset` take it.

### Showing notices

`NotificationServices.ForThisSystem` picks the way, and `AddPlatformServices()` registers it.

**Windows: a toast.**

- The Windows Runtime is called through combase, with no projection, since Platform builds for every system.
  - It makes an `XmlDocument` of the toast's XML: the generic template, with the title and the message as its two lines of text.
  - Then a `ToastNotification`, and a notifier for the app ID, which shows it.
  - Each call goes through the interface's table of functions, at the slot `windows.ui.notifications.idl` and `windows.data.xml.dom.idl` declare.
- **Windows groups toasts by app ID, and names them after it.** An app without an installer has no Start-menu shortcut to carry the ID.
  - So before the first toast, ModShelf registers its ID, `ModShelf.App`, for the current user.
  - The key is `HKEY_CURRENT_USER\Software\Classes\AppUserModelId\ModShelf.App`, and its `DisplayName` is `ModShelf`.
  - Other tools that show toasts for apps without an installer register their IDs the same way.
- **When Windows has notices turned off**, the warning says by whom: for ModShelf, for the user, or by group policy.

**macOS: `osascript`.**

- It runs `display notification`, which works without an app bundle.
- **The title and message are the script's arguments, never part of its text.** So nothing in them runs, and nothing in them needs escaping. The title goes first, because `osascript` reads options until its first argument, and ModShelf's titles never start with a hyphen.
- A run that takes more than 10 seconds is stopped.
- **macOS names these notices after Script Editor** until Phase 6 makes ModShelf a signed app bundle.

**Linux: libnotify.** It hands notices to the desktop's notification server over D-Bus, as the freedesktop.org specification describes, with the app name ModShelf. GNOME Shell and KDE Plasma run such a server.

**Elsewhere,** or on Linux without libnotify, notices are only logged. A warning says so once.

### Logging

The Platform notices take the event IDs 800–899:

| ID | Event |
|---|---|
| 800 | A notice couldn't be shown |
| 801 | This system can't show notices, so they're only logged |
| 802 | A notice that was only logged |

Event 705 joins the update checks' block: the notice of new updates couldn't be shown.

### Testing

**Platform tests:**

- **On every system:** the toast's XML, and `osascript`'s arguments, with the process replaced.
- **On macOS and Linux:** the runner that starts `osascript`, against `/bin/sh`.
- **On Linux:** libnotify, which shows the notice or logs why not.
  - CI's Linux runner installs `libnotify4`. It runs no notification server, so there the notice is logged.
  - Against a fake notification server, the server got the title, the message and the app name.
- **On Windows:**
  - registering the app ID, under a test ID that's removed afterwards;
  - every Windows Runtime call up to `Show`.

  `Show` itself needs a person to look.

**A real notice for a person to look at:** set `MODSHELF_TEST_NOTICES=1` and run `NotificationServicesTests.ShowsARealNotice`. It fails if the notice couldn't be shown.

**`LibraryCheckQuotaTests`** check Phase 4's goal end to end.

- **The setup:**
  - The CLI runs with the real Nexus Mods source.
  - The source talks to `FakeNexusApi`, which answers `validate.json`, `updated.json` and `files.json` in the shape of the recordings.
  - **It's served on 127.0.0.1 like the CLI tests' other fakes, not by WireMock.Net as proposed.** WireMock.Net brings the ASP.NET Core framework, which takes the Microsoft.Extensions assemblies out of the test folder. The CLI tests also run the CLI there as a process of its own, and it then couldn't start.
  - Each answer carries rate-limit headers that count down, and the API answers 429 once both allowances are used up.
  - The library has 200 watched mods across 5 games, each with its main file on disk.
- **A first check asks 200 times**, once per mod.
- **A later check asks each game's feed once**, then only the 3 mods with new files: 8 requests, and 3 new updates.
- **With 30 requests left in the hour and none in the day:**
  - the check asks 30 times;
  - the source holds the 31st back, so Nexus Mods refuses nothing;
  - the other games wait.

  The next check, once the hour starts again, checks the rest with 171 requests.
- **Moving the checked library moves all 200 files.** Every record points at its file under the new root, and the update found stays.

## Consequences

- **Phase 4 is done.** Nexus Mods allows 2,000 requests an hour and 20,000 a day. A first check of 200 mods asks 200 times, and a later one asks 5 times plus once per mod with changed files.
- **Clicking a notice does nothing yet.** Phase 5 can open the Updates screen from it.
- **Phase 6's installer:**
  - It should add a Start-menu shortcut with the app ID `ModShelf.App`, with an icon for the toasts.
  - It should remove the registry key on uninstall.
  - On macOS, the signed bundle can move notices to `UNUserNotificationCenter`, under ModShelf's own name.
- **CI never shows a real toast,** so a Windows notice is checked by a person, with `MODSHELF_TEST_NOTICES=1`.
- **The CLI registers the notices but never shows one.** It never runs background checks, and a Windows toast's app ID is only registered before a toast.
