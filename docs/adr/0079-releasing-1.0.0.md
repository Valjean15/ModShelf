# 79. Releasing 1.0.0

- Status: Accepted
- Date: 2026-11-06

## Context

The plan (step 19): upgrades from every released version's database and `settings.json`, 0.9.0 to 1.0.0, as tests,
plus the installed 0.9.4 updating itself; the desktop, MCP and installer checklists run with both accounts; a stable
channel, as 1.0.0 is the first release that isn't a pre-release, which the installed 0.9 copies must still see, checked
before tagging; signing decided before 1.0.0, or noted as known; then the README, screenshots, and the tag `v1.0.0`.

Since ADR 0038, the installed app has looked for updates on GitHub with Velopack's `GithubSource`, pre-releases
included, and every release so far, 0.9.0 to 0.9.5, was a pre-release.

## Decision

### Upgrades, as tests

- **Databases**: `ReleasedDatabasesTests` makes the database each release shipped with, migrated to its last
  migration (0.9.0 and 0.9.1 `AddNeedsFullCheck`, 0.9.2 `AddFileHashes`, 0.9.3 to 0.9.5 `AddPinsAndNotes`), fills it
  as that release did, in its own columns, with a linked account, a download, and a mod with a file, an update, and from
  0.9.2 its hash and from 0.9.3 a pin, a note, a skipped update and the file's download in the history; then opens it
  as 1.0.0 does. Everything is kept, every migration is recorded, and the library works: a note, a removal in the
  history, the mod put back. Saved mod details, a cache read from the site again, aren't filled.
- **Settings**: `ReleasedSettingsTests` reads the two shapes of `settings.json` the 0.9 releases documented, every
  setting each had: 0.9.0 to 0.9.2, with games' folders in `library.gameFolders`; 0.9.3 to 0.9.5, with `games` named by
  their key alone, previews, pictures and details. Saved by 1.0.0, games are named by their site, `gameFolders` goes,
  nothing is lost, and `quota` isn't written while at its default.
- 1.0.0 adds no migration: the last, `AddPinsAndNotes`, shipped with 0.9.3.

### A stable channel

- **The installed app takes stable releases alone** (`AppReleaseSources`: `prerelease: false`). The 0.9 copies took
  pre-releases too, so they see 1.0.0 and the stable releases after it. A pre-release, such as 1.1.0-beta.1, is tried by
  installing it from its release's page; a stable copy isn't offered it.
- Velopack asks GitHub for the newest 10 releases and looks no further, so a stable copy finds a stable release only
  among them: no more than nine pre-releases may follow the newest stable one.
- `GitHubReleasesTests` checks it with GitHub's answers made up (an `IFileDownloader` of the test's, which
  `AppReleaseSources.For` now takes), through Velopack's own `GithubSource` and update manager: 0.9.4 and 0.9.5, as
  they shipped, find 1.0.0 behind a pre-release; 1.0.0 finds 1.0.1 and passes over 1.1.0-beta.1; and isn't offered a
  pre-release alone.
- **The release workflow**: the upload already makes 1.0.0 a release rather than a pre-release, as its version is
  neither 0.x nor suffixed. The pack jobs' delta now comes from the newest stable release for a stable version, which
  the installed copies update from, and, for 1.0.0, with none yet, from the newest pre-release, 0.9.5; a pre-release's
  from the newest of either, as before.

### Signing, noted as known

- The installers stay unsigned for 1.0.0: signing needs a certificate bought for the purpose, a Windows code-signing
  certificate and an Apple Developer ID, which only the human can buy. The README says so where it describes the
  installers, with how to get past SmartScreen and Gatekeeper the first time, as the installer checklist already did.
  vpk signs while packing once the secrets exist (ADR 0039), so adding it later changes the pack jobs alone.

### Checks by hand

- The installer checklist's new section 4: before the tag, on Windows, an installed 0.9.4, packed from its tag, makes a
  library (an account, a file, a watched mod, a pin, a note, a skipped update, a game's own folders and previews, the
  light theme), then updates to 1.0.0, packed from the commit to tag, from a folder, keeping it all; after the release
  is published, a 0.9 copy installed from GitHub finds 1.0.0 there and updates to it.
- The desktop, MCP and installer checklists are run with a Premium and a free account by the human, including the
  desktop items still open from Phase 5. Tagging `v1.0.0` and publishing its draft are the human's.

### README

- The status says 1.0, the first stable release; Releasing says which versions are releases and which pre-releases;
  the self-update says it leaves pre-releases out.
- No screenshots were added: the scratch library here, without an account, shows the games by their keys and
  initials, as the sites give their names and banners. Screenshots from a library with an account linked are left to
  the human.

## Consequences

- Installed 1.0 copies see only stable releases; people trying a pre-release install it by hand, and get the stable
  release that follows it.
- The 1.0.0 release notes list the `feat:` and `fix:` commits since 0.9.5.

## Testing

- The upgrade tests above, and `GitHubReleasesTests`; `VelopackAppReleasesTests` now expects stable releases alone.
- 6 deliberate bugs, all caught: pre-releases taken again; a migration's default pinning every mod, another giving the
  files before 0.9.2 an empty hash; `gameFolders` kept, a game's old folders lost, and a game by its key alone given
  another site.
- The workflow's new step was run here with a stand-in for vpk: 1.0.0 with only pre-releases before it takes 0.9.5;
  1.0.1 takes 1.0.0 over a newer 1.1.0-beta.1; 1.1.0-beta.2 and 0.9.6 take the newest of either; with nothing to
  download, a notice, as before.
- By hand: `docs/manual-checklist-installer.md` section 4.
