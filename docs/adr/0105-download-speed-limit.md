# 105. A download speed limit all downloads share

## Context

ModShelf's downloads take what the connection gives. On a slow or shared connection, an **Update all** or a batch from
Browse leaves nothing for anything else. Until 1.1.0 there was no way to slow them down short of pausing them.

The human approved, for 1.1.0 (feature 8 of the plan), a download speed limit: set on Settings › Library and from the
command line, turned on and off from the Downloads screen, and taken by a running queue without restarting it. That
approval covers the change to Application's public surface: `ISettingsService.SetDownloadSpeedLimitAsync`, and
`AppSettings.DownloadSpeedLimit` with `DownloadSpeedLimitOn`.

## Decision

### One limit, for all of them together

The limit is the most ModShelf's downloads take **together**, in bytes a second, not a limit for each. The queue runs
several downloads at once, and someone who limits them wants the connection to stay usable, whatever the queue holds.
It's from 16 KB a second, below which a download would seem stuck, to 10,000 MB a second.

Only one process runs a library's queue (its lock file), so the limit of the process that runs it is the library's.

### Where it's kept

`settings.json` keeps it as people write it, in kilobytes or megabytes a second in powers of 1024, as file managers
show sizes: `"downloads": { "speedLimit": "2MB" }`, or `"1536KB"` for a speed that isn't a whole number of megabytes.
Domain's `DownloadSpeed` reads and writes that text, for the settings file, the command line and the desktop app alike.

Turning the limit off keeps its speed, so the Downloads screen's switch can turn it on again: `"speedLimitOn": false`
is written beside it while it's off, and left out while it's on. `AppSettings.DownloadSpeedCap` is the limit in force,
null while there's none or it's off. `SetDownloadSpeedLimitAsync(bytesPerSecond, applies)` sets both; null takes the
limit away, and a speed out of range is refused.

### Where it's applied

Every site's download ends in Infrastructure's `HttpFileTransfer`, which copies the response to the partial file, so
the limit is applied there, by one `DownloadSpeedLimiter` the process shares (a singleton):

- **A token bucket** refilled at the limit, holding a quarter of a second's bytes. After each read, the transfer takes
  the bytes read from it; when that leaves the bucket in debt, the transfer waits, on `TimeProvider`, until the debt
  is paid. Several downloads draw from the same bucket, so together they keep to the limit.
- **Reads are sized** to about a quarter of a second's bytes under a limit (at least 4 KB, at most the 80 KB buffer),
  so a low limit doesn't arrive in bursts and progress stays smooth. Waiting after a read rather than before keeps the
  transfer's reading, resuming and progress as they were: the bytes are written and counted, then the transfer waits.
  Meanwhile the connection's buffers fill, and TCP's flow control slows the server down.
- **Read again every 2 seconds** from the settings, which aren't cached, so a change from anywhere, the desktop app's
  switch, `settings set` from the command line, or an agent's `settings_set`, reaches a running download within
  seconds. A settings file that can't be read leaves the limit it had, and the log says so. A changed limit starts with
  a full bucket.
- What a waiting download on a site's page takes from the browser's folder was downloaded by the browser, so it isn't
  limited; nor are pictures and the sites' API calls, which are small and go through their own clients.

### Showing and changing it

- **Settings › Library › Download speed:** **Limit downloads to**, a speed and its unit, KB/s or MB/s, each change
  saved at once. Changing the unit keeps the speed, rounded to a tenth, within what that unit allows. A limit is shown
  in whole megabytes when it's a whole number of them, otherwise in kilobytes.
- **The Downloads screen,** once a limit is set, says it under its heading, "Limited to 2.00 MB/s" or "Not limited
  (2.00 MB/s when on)", with a switch that turns it on and off. It's read again each time the screen shows. One
  ViewModel, `DownloadSpeedLimitViewModel`, serves both screens, so each shows what the other changed.
- **The command line and the MCP server:** `downloads.speedLimit` takes a speed, such as `2MB` or `512KB`, which sets
  it and turns it on; `off`, which keeps the speed; or `on`, which turns the speed kept on again. `settings show`
  lists `2MB`, `2MB (off)`, or `off` by default, and `settings reset` takes it away. It isn't one of the settings of
  agents' access, so an agent may change it.

## Consequences

- The speed and time left a download shows follow the limit, since they're measured on the bytes written.
- The limit holds over a few seconds; a quarter of a second's bytes can go through at once when it's set, changed or a
  download starts after a pause.
- A limit set while a download waits on its bucket takes effect at its next read.
- Log events 112 (limited to a speed), 113 (no longer limited) and 114 (the limit couldn't be read) are the
  limiter's; 989 and 990 are the desktop app's, saving and reading it.

## Testing

- `DownloadSpeedTests` (Domain): reading and writing speeds, and what's refused.
- `SettingsServiceTests` and `JsonSettingsStoreTests`: setting, turning off, keeping and clearing the limit, and a
  settings file with a speed that isn't one.
- `DownloadSpeedLimiterTests`, on a `FakeTimeProvider`: the wait for bytes beyond the bucket, the share between
  downloads, read sizes, reading the limit again every 2 seconds, and keeping it when it can't be read.
  `HttpFileTransferTests.DownloadsUnderTheSpeedLimitCountingEachReadBeforeItWaits`: a transfer counts its bytes, then
  waits.
- `DownloadSpeedLimitViewModelTests`, `DownloadsSpeedLimitTests`, `SettingsViewModelTests` and
  `SettingsCategoriesTests`: the box, the units, the Downloads screen's line and switch, and searching for it.
- `SettingsCommandTests`: `downloads.speedLimit` set, off, on, listed, reset and refused.
- By hand, with a 6 MB file from a server on this computer: 1.7 seconds without a limit, 4.4 at 2 MB/s and 7.5 at
  1 MB/s, start-up included. At 512 KB/s, turned off with `settings set` 3.5 seconds in, the download ended at 5.9
  seconds rather than about 13.
