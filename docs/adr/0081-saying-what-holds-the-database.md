# 81. Saying what holds the database

- Status: Accepted
- Date: 2026-11-09

## Context

The human reported (2026-11-09) that the gear beside a game's name in the Library froze the app for about 30 seconds
before the game's settings opened, and that going back to a game of many mods was slow; and asked why several
`ModShelf.Cli` processes were running.

Looked at here, with a library of 630 mods and a second process holding the database, the frozen app's stack was the
Settings screen reading the library on the UI thread, waiting in SQLite for the other process's lock: Microsoft.Data.Sqlite
has no asynchronous I/O, so EF Core's `…Async` queries block the thread that calls them, and `SqliteBusyTimeout` lets a
command wait up to its connection's 30 seconds. The human's logs had no "database is locked" errors, so each wait ended
in time, and nothing said what held the lock: the logs say neither which process wrote them nor that a command was slow.
The processes were two `mcp` servers Claude Desktop started a second apart, its known double start on Windows, and the
`mcp --http` the app runs (ADR 0080).

The plan for 1.0.1 (step 1) makes the logs say so, before the fixes (WAL, step 2; the database off the UI thread, step 3).

## Decision

### Slow commands

- **`SlowCommandLog`** (Infrastructure's `Persistence/`), an EF Core interceptor beside `SqliteBusyTimeout`, warns of any
  library database command that takes a second or more (`DefaultThreshold`), from EF Core's own timing of it:
  - "The library database took 12.3 s for SELECT from Mods" (event 205);
  - "The library database failed INSERT into DownloadJobs after 30.0 s" (206), for one that fails after as long.
- **A transaction's start and commit count**, as `BEGIN` and `COMMIT`: a change made in a transaction, such as the
  library's (`LibraryRepository.UpdateAsync`, `BEGIN IMMEDIATE`), waits for the lock as its transaction starts, and a
  commit can wait for readers.
- **What it was, never its values:** `CommandSummary` gives the statement and the first table it names, such as
  `SELECT from Mods`, `INSERT into DownloadJobs` or `UPDATE Mods`; only an identifier is kept where a table would be, so
  a literal in a command, or a parameter's value, never reaches the log.

### The start line

- **Each process's logs start with what it is** (Platform's `Diagnostics/ProcessStartLog`, registered by
  `AddProcessStartLog(runs)`): "ModShelf 1.0.1 (abc1234): the command line, mcp --http --port --parent, process 4242,
  started by ModShelf.Desktop (process 4100)" (event 1307), or, when the system doesn't say which process started it,
  without it (1308). A process whose parent has ended says "started by a process that has ended".
- **Said first,** in the host's `StartingAsync`, registered before the library database, so it's said even when the
  database can't be opened.
- **What it runs:** "the desktop app"; for the command line, the command's words and the names of the options given,
  never their values (`RunningCommand`, set by `GuardedAction` for the command's own work, and read when it builds its
  hosts): a `download` of an `nxm://` link is "download --to". A host the command line builds outside a command is "the
  command line". The MCP server, which builds a second host to run downloads, says it once more then.
- **The parent** (`ParentProcess`): on Windows, `NtQueryInformationProcess`; on Linux, `/proc/self/stat`; on macOS,
  `getppid`; its name from .NET's `Process`. No package.

## Consequences

- Every run of the command line writes its log file's line, so a log file is written for commands that logged nothing
  before.
- With the human's logs after 1.0.1, a freeze's cause shows as a slow command, and which process held the lock as
  another process's commands about then; the process IDs match Task Manager's and the PowerShell in the plan.
- The plan's capture of a freeze (`dotnet-stack`) stays for what the logs can't say.

## Testing

- Infrastructure: a change waiting more than a second for another connection's write is logged with its time and
  `INSERT into DownloadJobs`, never the mod's name; a library change waiting to start its transaction is logged as
  `BEGIN`; commands under a second aren't; a command that fails after waiting is logged as failed; the summaries of
  EF Core's commands, with a literal and a subquery, and of commands without a table.
- Platform: the parent is another process, running, with its name; on Linux, it's what `/proc/self/status` says; an
  ended process has no name; `/proc`'s line read past a name with spaces and brackets; the line as it's logged; its
  registration.
- The command line: what a command's line says, its words and options, never a value (an `nxm://` link's key, a
  folder); a command's logs start with it, before the database's; started as a process of its own, it names the test as
  the process that started it, on each system CI runs.
- 18 deliberate bugs, all caught.
