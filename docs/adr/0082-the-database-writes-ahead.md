# 82. The database writes ahead

- Status: Accepted
- Date: 2026-11-09

## Context

The library database used SQLite's default rollback journal. There, a connection changing the database keeps every
reader out while it commits, and a change waiting to commit while another connection reads keeps new readers out too.
The desktop app, the command line and the MCP servers agents' apps start (ADR 0080, 0081) are processes of their own on
one library, and a read waits up to 30 seconds for such a lock (`SqliteBusyTimeout`). On the UI thread, that was the
30-second freeze the human reported (2026-11-09) on a game's settings.

SQLite's write-ahead log (WAL) appends changes to a file beside the database, `library.db-wal`, and readers read the
database as it was when they started, so a read never waits for a change, nor a change for a read; only two changes
still wait for each other. The human chose WAL (2026-11-09), knowing it adds files beside the database and doesn't
work in a network folder.

## Decision

- **Set once, when the library database is opened** (`LibraryDatabaseMigrator`, after bringing the schema up to date):
  `PRAGMA journal_mode = WAL`. SQLite keeps the mode in the file, so every later connection, in any process and any
  version of ModShelf, 0.9 and 1.0.0 included, opens it so. Each start asks the mode first, and changes nothing when it's
  set.
- **Said once:** "The library database now writes ahead of its file (WAL), so reading it never waits for a change"
  (event 207).
- **When SQLite can't**, the database keeps its journal and the log says so, with the journal it keeps (208). **When
  another program holds the database** as ModShelf starts, the switch, which needs the database to itself, waits as
  any command does, then gives up without stopping the start (209); the next start tries again.
- **`synchronous` stays FULL**, SQLite's default: each commit is on disk when it returns, so a change survives a power
  cut. The plan suggested NORMAL, which is faster but can lose the last changes then; the freeze doesn't need it.
- **The README** says the database is three files while ModShelf runs (`library.db-wal`, `library.db-shm`), which a
  copy of the library takes together, and that it belongs on this computer's own disks: WAL shares memory between
  processes, which a network folder can't.

## Consequences

- A read, such as a screen's, no longer waits for the MCP server's or the command line's changes, and the reverse.
  Two changes still take turns, which `SqliteBusyTimeout` still waits for.
- `LibraryDatabaseWaitTests` checked that a change waits for another connection's read, which is no longer so: each of
  its four tests now holds a change in the other connection instead, which a change still waits for, and checks the
  same thing, that the change is saved once the other ends. `WriteAheadTests` checks that a read holds nothing.
- SQLite folds the log into the database when the last connection closes, so the extra files go when ModShelf ends,
  and stay, with changes in them, after a crash, until ModShelf opens the database again.

## Testing

- A new database writes ahead, and says so; a database in another journal, as before 1.0.1, writes ahead once opened,
  with its rows kept.
- A change doesn't wait for another connection's read, which still sees the database as it was; a read doesn't wait for
  another connection's exclusive change, which keeps every reader out of a database that doesn't write ahead.
- While another connection reads a database in another journal, the start keeps its journal, says so, and goes on; the
  next start writes ahead.
- A change made during another connection's change is saved once that ends (`LibraryDatabaseWaitTests`, now with a
  change held).
- The released databases of 0.9.0 to 0.9.5 still open with everything kept (`ReleasedDatabasesTests`, unchanged).
- 5 deliberate bugs, all caught.
