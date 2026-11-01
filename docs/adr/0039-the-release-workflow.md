# 39. The release workflow

- Status: Accepted
- Date: 2026-11-01

## Context

The plan makes tagging `v0.9.0` the whole release step (ADR 0035): a GitHub Actions workflow on the tag builds, tests,
packs for Windows, macOS and Linux, and attaches the packages to the release. ADR 0038 made the packages with
`build/pack.ps1` and `build/pack.sh`, and the installed app updates itself from the GitHub releases.

The human approved the workflow as listed below, with a draft release published by hand and a dry run; release notes
made from the commits; Apple silicon only, as planned; and signing left to step 6.

## Decision

- **`.github/workflows/release.yml`** runs on a pushed tag like `v0.9.0`, and on **Run workflow**, a dry run that does
  everything but the release. Its jobs:
  1. **Test**: the CI workflow, which the release calls (`ci.yml` gains `workflow_call`), on the three systems.
  2. **Version and notes**, from the whole history (`fetch-depth: 0`): the version MinVer computes for the commit, and
     the notes. A tag that isn't that version, such as `v1.0` or a tag MinVer passes over, stops the release.
  3. **Pack**, once the tests pass: `win-x64` on Windows with `pack.ps1`, `linux-x64` on Linux and `osx-arm64` on
     macOS with `pack.sh`, at that version and with those notes. Each first downloads its channel's newest published
     release with `vpk download github`, so that vpk packs a delta beside the full package. The packages stay with the
     run for 14 days.
  4. **Draft the release**, for a tag only: `vpk upload github --merge` uploads one channel after another into a single
     draft release, "ModShelf 0.9.0", a pre-release while ModShelf is 0.x or the version is one. Only this job may write
     to the repository (`contents: write`); the token is the run's own `GITHUB_TOKEN`.
- **The draft is published by hand.** Installed apps look only at published releases, so nothing reaches them before
  someone has checked the draft's notes and files.
- **Release notes come from the commits**: `build/release-notes.sh` lists the subjects of the `feat:` commits under
  "New" and of the `fix:` commits under "Fixed", since the newest `v` tag the release's parent commit reaches, without
  scopes, `!` or merges, oldest first. With no `feat:` or `fix:` commits, the notes say the changes are behind the
  scenes; with no earlier tag, that it's the first release. The packages carry the notes, so the About card shows them
  with the update (ADR 0038), and vpk puts them on the release. Editing the draft's text afterwards changes only the
  GitHub page. The script runs in bash 3.2, macOS's own.
- **One release run per tag at a time** (`concurrency`), so a second run waits rather than uploading over the first.
- **Fixes to the pack scripts**, found writing the workflow: reading the version from MinVer now restores first, which
  a fresh clone needs; and `pack.ps1` takes `-Output` and `-Notes` from PowerShell's current folder, which .NET's own
  doesn't follow after `cd`.

## Consequences

- Releasing is `git tag v0.9.1`, `git push origin v0.9.1`, then publishing the draft on the Releases page once the run
  is green. The README says so.
- The first release, v0.9.0, has no delta and its notes say it's the first. From then on, an update downloads a delta.
- A draft left unpublished is never the base of a delta: the pack jobs' token can't see drafts.
- `feat:` and `fix:` subjects are now what users read, so they're written for users; `CONTRIBUTING.md` says so.
- Signing comes in step 6, as secrets the pack jobs pass to vpk. Intel Macs aren't covered; adding them is a matrix entry
  (`macos-15-intel`, `osx-x64`) and a channel in the upload loop.

## Testing

- Architecture tests run `build/release-notes.sh` over repositories of their own: the first release, features and
  fixes since the release before (with scopes, `!` and other types), fixes only, nothing to see, and a tagged release
  starting after the version tag before its own, past a tag that isn't a version. They run on Linux and macOS: on
  Windows, `bash` may be WSL's, which can't see the test's folder. Ten deliberate bugs in the script each fail one.
- actionlint 1.7.12, with shellcheck, finds nothing in either workflow; shellcheck finds nothing in the scripts.
- Locally: the notes of a fresh clone with a `v0.9.0` tag, packed with `pack.sh --notes` for `linux-x64`, are the
  notes in the package's feed; both pack scripts read the version on a fresh clone; `pack.ps1` writes to the folder
  given relative to PowerShell's current one.
- Not run here: the workflow itself, which needs GitHub. Its first run is a dry run from the Actions tab, then a tag.
