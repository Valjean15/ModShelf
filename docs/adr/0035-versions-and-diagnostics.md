# 35. Versions and diagnostics

- Status: Accepted
- Date: 2026-10-30

## Context

Phase 6 ships ModShelf to people who didn't build it. Each build then needs a version that says which commit it came
from, and a bug report needs what the app knew when it went wrong.

Until now every assembly was version 1.0.0.0, the SDK's default. Nexus Mods was sent `1.0.0` in its
`Application-Version` header whatever the code was, and nothing on screen said which build was running.

A bug report needs the version, the system, the settings and the state of the library, the queue and the accounts. The
log files hold what happened, but not what was set, and the human shouldn't have to read logs to find out where they
are. Neither may hold keys, account names, links or anything the user would rather not paste into a public issue (the
project's rules on logging).

## Decision

- **The version comes from the git tag** (MinVer 8.0.0, a build-only package, `PrivateAssets="all"`, for every project
  in `Directory.Build.props`). Tags look like `v0.9.0`. A tagged commit is `0.9.0`; the commits after it are
  `0.9.1-alpha.0.N`; before the first tag, `0.9.0-alpha.0.N`. The informational version adds the commit after a `+`. A
  build without git, such as from a source archive, is `0.9.0-alpha.0`, and MinVer warns. The warning is MSBuild's, not
  the compiler's, so warnings-as-errors leaves it a warning.
- MinVer leaves the assembly version at `0.0.0.0`, so ModShelf reads the **informational version**:
  - Sources.Nexus sends its part before the `+` as `Application-Version`, such as `0.9.0`.
  - Application's `AppInfo` holds the version and the commit's first seven characters, such as `0.9.0 (a60d76b)`. The
    hosts register the one for their own assembly.
- **Application's `IDiagnostics` writes the report**, a line per part: the version, when it was written, the system and
  runtime, the folders, the settings, the library's counts and its oldest check, the queue by status, and each site's
  account as linked or not, with how it's linked and its tier, and whether the system keeps the secrets.
  - It holds no keys, account names, links, mod names or file names. The home folder shows as `~`, which leaves the
    user's name out of the paths.
  - It's in English, like the logs, for whoever reads the bug report.
  - A part that can't be read says so with the error's type only, since a message could hold a path, and the rest is
    still written. Event 1300 logs it.
- **Where the app keeps its own files is a port**, `IAppFolders` (data and logs), in `Application/Diagnostics/Ports`,
  which Infrastructure registers where it chooses them.
- **The Settings screen's last card is About**: the version, a line on what a bug report needs, **Copy diagnostics**,
  which puts the report on the clipboard through Presentation's `IClipboard`, and **Open log folder**, which says where
  the folder is when it can't open it.
- **`ms diagnostics`** prints the same report, for the CLI and for a bug where the desktop app won't start.
- Event IDs 1300–1399 belong to the app itself: diagnostics, the `nxm://` handler and self-update.

## Consequences

- Releasing a version is tagging a commit `vX.Y.Z`. The release workflow (Phase 6) has to fetch the whole history
  (`fetch-depth: 0`): from a shallow clone, MinVer can't see the tag. CI's own builds stay shallow, and their version
  doesn't matter.
- Nexus Mods sees the real version, so it can tell builds apart if one misbehaves.
- The report grows with the app. A new part follows the same rule: counts and choices, never names, keys or links.

## Testing

- `AppInfo` from informational versions with and without a commit, a short commit, and none at all.
- The report from fakes: each part's wording, one and many, the home folder as `~` and a folder outside it as it is, the
  account linked and not, the secrets kept by the system or in files, no account, mod or file name anywhere in it, and a
  part that throws written as unreadable while the others are still there.
- Nexus Mods' header test compares with the informational version before its `+`.
- The About part: the version, the report on the clipboard and the note, the log folder opened or named, and a failure
  shown and logged. The Settings screen ends with it.
- Infrastructure registers its data and log folders; `ms diagnostics` prints the report with the test's folders.
