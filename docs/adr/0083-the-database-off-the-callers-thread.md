# 83. The database off the caller's thread

- Status: Accepted
- Date: 2026-11-09

## Context

Microsoft.Data.Sqlite has no asynchronous I/O. EF Core's `…Async` methods on it read and write the file, and wait for
another connection's lock, on the thread that calls them, and return a task that's already done. So every repository
call from a screen ran on the UI thread: a game's settings read the whole library twice there, the Library read each
mod's saved details one by one there, and the window stopped for as long as each took, or as long as SQLite waited for
a lock, up to 30 seconds (ADR 0081).

Writing ahead (ADR 0082) ends the reads' waits for changes, but not the time a read itself takes, nor the waits of a
change for another change; and any thread that calls a repository, the UI thread above all, should get its call back at
once, whatever the database does.

## Decision

- **`ThreadPoolSwitch`** (Infrastructure's `Persistence/`), an awaitable: `await ThreadPoolSwitch.Now;` goes on in a
  work item of the thread pool, whatever thread awaited it, and never completes at once, so even a caller on the pool
  gets its call back as soon as it's made. The rest of the method stays on the pool, every await in Infrastructure being
  `ConfigureAwait(false)`; the caller's own await comes back to its thread as usual.
- **Each of the 14 methods of the five repositories** (`DownloadJobRepository`, `LinkedAccountRepository`,
  `LibraryRepository`, `ModDetailsRepository`, `LibraryHistoryRepository`) awaits it just before it opens its context:
  one line each, in the one project that knows SQLite works so. Application, Presentation and the screens change
  nothing.
- **Not the start:** `LibraryDatabaseMigrator` runs as the host starts, before any window shows, and stays as it is.
- Rather than `Task.Run` around each method's body, which would move every body a level in, for the same effect.

## Consequences

- A screen's call to the library, the queue, the accounts, the saved details or the history returns to the UI thread at
  once; the screen shows what it has and fills in what the database gives when it answers. A slow read still takes its
  time, which step 4 of the plan shortens.
- Each call costs a hop to the pool, nothing next to opening a context.
- A synchronous wait on a repository's task would no longer complete it on the waiting thread; nothing in ModShelf waits
  so ("async all the way").

## Testing

- Each of the 14 methods, called from a thread of its own that's no pool's, opens its connection on another thread.
- A change called while another connection holds a change returns to its caller at once, still waits for the other,
  and is saved once it ends.
- The repositories' own tests, and every other test of the database, unchanged.
- 15 deliberate bugs, all caught.
