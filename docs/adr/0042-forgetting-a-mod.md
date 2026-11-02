# 42. Forgetting a mod

- Status: Accepted
- Date: 2026-11-02

## Context

The library had no way to remove a mod. Unwatching keeps a mod with files, and deleting its files one by one deletes
them from disk too. The human reported mods the library still listed after their files were deleted by hand, such as
DD2gether and two packs, and asked for a way to remove a mod from the MCP server.

The human approved: forget the mod, files stay; `IModLibrary.ForgetAsync` by link and by reference; `library forget`
in the CLI, `library_forget` in the MCP server, and **Remove from library** in the desktop app, which says the files
stay.

A finished download stays in the download queue until it's cleared, and a host that runs downloads records, as it
starts, every completed download under the library root that the library lacks and that's still on disk (ADR 0019).
A forgotten mod's files are still on disk, so the next desktop app or `download` command would have recorded them
again, and the mod would be back.

## Decision

- **`IModLibrary.ForgetAsync(ModRef)`** takes the mod out of the library, with its file records and its updates, in one
  change, and publishes it to `ChangesAsync`. It never touches a file. **`ForgetAsync(Uri)`** finds the mod as
  unwatching does: a library mod by its page there, without asking its site, otherwise as the site says.
- **A mod not in the library** is a `KeyNotFoundException`. The CLI and the MCP server say so as unwatching does, as a
  result, not an error: there's nothing to forget.
- **A download of one of its files into the library that isn't finished**, waiting, paused or failed included, stops
  it with an `InvalidOperationException`, as moving the library does: once finished, the download would bring the mod
  back. Downloads of its files elsewhere, with `--to`, don't count.
- **Its completed downloads into the library leave the queue**, before the mod leaves the library, so no start records
  them again. Cancelled ones stay, since nothing records them. Only the process that runs the queue changes it (ADR
  0027), so Application gains the internal `ICompletedDownloads`, which the scheduler implements: in that process it
  removes them from the queue and the database; in one that doesn't run the queue, such as the CLI's or the MCP
  server's own host, from the database only.
- **The MCP tool** `library_forget(link)` is marked not destructive, since no file changes, and idempotent. Its result
  says whether the mod was forgotten, in a sentence too, and gives the mod as the library had it, with the paths where
  its files stay.
- **The desktop app** has **Remove from library…** under an expanded mod's files, with a confirmation that says the
  files stay and the completed downloads leave the Downloads list. A refusal is said in a message.

## Consequences

- A forgotten mod's files are no longer the library's: moving the library leaves them behind, and downloading the same
  file again saves a second copy beside it, with the file's ID in its name. Rescanning (0.9.2 step 5) will bring files
  like these back into the library.
- Forgetting from the CLI or the MCP server while the desktop app runs the queue: the app's Downloads screen lists the
  mod's completed downloads until it starts again or clears them, and its Library screen shows the mod until it starts
  again, as with every change another process makes. In the app itself, the Downloads screen keeps listing them until
  **Clear finished** or a restart.
- The MCP server has 20 tools.

## Testing

- Application:
  - A mod forgotten with its files and updates, its files left on disk, the change published and logged.
  - Found by its page without asking its site, or by the site; refused when it isn't in the library.
  - Each unfinished status of a download into the library stops it, and one elsewhere doesn't; the count in the
    message.
  - Its completed downloads into the library cleared, and only those; a queue that can't be cleared leaves the mod.
  - A forgotten mod's download isn't recorded again at the next start.
  - The scheduler clears chosen completed jobs while it runs the queue, while another process runs it, and never
    started.
- CLI: `library forget` with no, one and two files, which stay on disk; a mod not in the library; unreadable settings;
  bad links; a real download forgotten, then another download run, which doesn't record it again; and a waiting
  download that stops it. The MCP server: `library_forget` with its result checked against its schema, again, a mod
  without files, the saved queue emptied, and a download under way that stops it.
- Presentation: confirming, not confirming, each refusal, a mod gone meanwhile, and an unexpected failure.
- By hand: `docs/manual-checklist-desktop.md` 9.5, `docs/manual-checklist-mcp.md` 3.4.
