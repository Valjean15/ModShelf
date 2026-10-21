# 3. Download scheduler on a single message loop

- Status: Accepted
- Date: 2026-10-21

## Context

The scheduler runs several downloads at once, within a global limit and a per-source limit. At the same time the user can pause, resume, retry or cancel any job, and the UI and the CLI watch progress. Each download reports progress many times per second from its own thread. Jobs must be saved consistently so that a restart resumes them, and the EF Core `DbContext` behind the repository is not thread-safe.

## Decision

`DownloadScheduler` (in `ModShelf.Application`) is a `BackgroundService` that also implements `IDownloadService`. All job state belongs to one loop that reads messages from a single channel:

- **Commands** from callers (enqueue, list, pause, resume, retry, cancel), each answered through its own `TaskCompletionSource`.
- **Reports** from running downloads (transfer started, advanced, verifying, link expired, attempt finished).

Only the loop changes `DownloadJob` entities or calls the repository, so there are no locks, and every saved state is complete. The downloads themselves (resolving a ticket and transferring the file) run outside the loop and never touch a job; they only send reports.

Other rules that follow from this:

- **Pause and cancel of a running job complete when the download has actually stopped** and the job has been updated, not when the request is sent.
- **Progress is published at most once per `ProgressInterval` per job and is not saved.** The partial file on disk is what a resumed download relies on; status changes are always saved and published.
- **Watchers get their own unbounded channel** (`IDownloadService.WatchAsync`). Subscribing happens immediately, and cancelling the token ends the stream without an exception.
- **On start, jobs a crash left in Resolving, Downloading or Verifying are requeued.** On stop, running downloads are cancelled and requeued, so they continue on the next start.
- **When the host stops before `ExecuteAsync` has run**, which `BackgroundService` allows, `StopAsync` closes the inbox itself, so waiting and later requests fail with `InvalidOperationException` instead of hanging.

The plan's `IDownloadQueue` is named `IDownloadService`, because analyzer CA1711 reserves the "Queue" suffix for collection types.

## Consequences

- The loop is the only writer of job state, so new features that change jobs must go through a message rather than touching a job directly.
- One slow repository call delays every message behind it. That's acceptable for a local SQLite file; if it ever isn't, saving can move to a dedicated writer without changing the public interface.
- Tests drive the scheduler through hand-written fakes (an in-memory repository, a transfer the test ends by hand, a scripted resolver) and a fake clock, and run every case without real delays.
