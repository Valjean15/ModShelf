# 59. The MCP server after a day's use

- Status: Accepted
- Date: 2026-11-03

## Context

The human used the MCP server with Claude Desktop against their library, and reported (2026-11-03) what was
fixed since 2026-11-01 and what was still open. Checked against the code and the server they ran:

- The server was older than step 13a, and the tool list their agent saw older still: it showed `library_list` without
  its settings, though the server took `offset`, `limit`, `compact` and the filters, and had `library_forget` and
  `library_rescan`. So "no paging", "no way to remove a mod", "files placed by hand aren't recognized" and "deleted old
  versions aren't mentioned" (which `library_changes` says since 13a) came from a stale tool list, which the agent's app
  keeps until it's restarted.
- Two files had no SHA-256. A rescan of each mod read them at once: they were recorded before ModShelf kept hashes, and no
  rescan had reached them. The rescan of Female Nude Mod found no other file in its folder, so the Blend 1 file isn't
  where the library looks.
- **Replaced by, backwards.** `mod_show` gave Dynamic Menu Overhaul's newest file as replaced by an older, archived one.
  Nexus Mods lists some update links back in time (the recorded SMAPI list has 13 of its 294), which the library has
  ignored since 2026-10-30 (`Replacements`), but `mod_show` passed on as they are.
- **"Downloading" when it's done.** `download` described each download as the request left it, queued, while a small
  file can be downloaded, or a download fail, before the call ends.
- A page of `library_list` was large; it had filters for watched mods and mods with updates, but none for missing files.

The human chose (2026-11-03): links back in time left out in the Nexus source, rather than in `mod_show` alone; a
`missingOnly` filter, with descriptions that say what the results hold; and the two rescans run from here.

## Decision

- **Nexus:** `GetFilesAsync` leaves out an update link to a file uploaded before the one it says it replaced, when both
  are listed. A later link of the same file forward still counts; one between files uploaded at the same time, or to a
  file not listed, is kept. `Replacements` stays, for any site.
- **MCP `download`:** once every link is asked for, the results' downloads are read again from the queue (this server's,
  or the database the desktop app keeps), and each message says what the download is then: downloaded (into the
  library, with `library_changes` for the version it replaced), failed (with the reason, and `downloads_retry`),
  cancelled, paused (`downloads_resume`), waiting for the user, or downloading. "This MCP server downloads it" is said
  only of one still under way. The summary counts the downloaded and the failed apart. When the queue can't be read
  again, the results stay as the requests left them.
- **MCP `library_list`:** `missingOnly`, the mods with files gone from disk. Its description names the page settings,
  the files' sizes and SHA-256, the filters, and `library_rescan` for a file without a SHA-256; the server's instructions
  say the same in a line. `mod_show`'s description says `replacedBy` is a newer file, by the site's word.

## Consequences

- A `download` call reads the queue once more before it returns.
- A file that was an update of another uploaded later still says so; a mod's newest file isn't replaced by an older one.
- An agent's app shows the tools as they were when it started: after an update, it's restarted, and a new chat begun.

## Testing

- Nexus: the recorded list's links back in time left out (281 of 294 kept), each kept link going forward; a list by
  hand with a link back in time beside one forward for the same file, one at the same time, and one to a file not
  listed.
- MCP: a download the desktop app took as queued said as the queue has it when the call ends, completed, failed,
  paused or downloading, and completed outside the library; several links counting the downloaded and the failed;
  `missingOnly` with a file deleted, a watched mod without files left out, and none missing. Two tests that download
  hold the file part-way, so the result finds it under way.
- 18 deliberate bugs, all caught; one got through at first, a download finished outside the library, and a test was
  added for it.
- By hand: `docs/manual-checklist-mcp.md` 1.1, 1.2, 2.1, 2.6 and 2.6a.
