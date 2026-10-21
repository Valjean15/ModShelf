# 2. Download job state machine

- Status: Accepted
- Date: 2026-10-21

## Context

The implementation plan defines a `DownloadJob` with the statuses Queued, Resolving, AwaitingHandoff, Downloading, Verifying and Completed, with Paused, Failed and Cancelled as exits. It also requires that a download survives a restart and resumes, that an expired link mid-download leads to a fresh link instead of a failure, and that download links are never cached. The plan doesn't say which transitions are allowed, so this ADR records them.

## Decision

`DownloadJob` in `ModShelf.Domain` allows exactly these transitions. Any other call throws `InvalidOperationException` and leaves the job unchanged.

| Method | Allowed from | Leads to |
|---|---|---|
| `StartResolving` | Queued, AwaitingHandoff, Downloading | Resolving |
| `AwaitHandoff` | Resolving | AwaitingHandoff |
| `StartDownloading` | Resolving | Downloading |
| `ReportProgress` | Downloading | Downloading |
| `StartVerifying` | Downloading | Verifying |
| `Complete` | Verifying | Completed |
| `Pause` | Queued, Resolving, AwaitingHandoff, Downloading | Paused |
| `Resume` | Paused | Queued |
| `Requeue` | Resolving, Downloading, Verifying | Queued |
| `Fail` | Resolving, AwaitingHandoff, Downloading, Verifying | Failed |
| `Retry` | Failed | Queued |
| `Cancel` | anything except Completed and Cancelled | Cancelled |

The choices behind the table:

- **Resuming always goes back to Queued.** A resumed, retried or requeued job resolves a fresh link, because links expire and are never cached. The bytes already on disk are kept in `BytesDownloaded`, and `StartDownloading` takes the offset to resume from, so the transfer continues where it stopped.
- **An expired link goes Downloading → Resolving**, not to Failed.
- **Requeue is separate from Pause.** Pause is the user's choice and persists across restarts. Requeue is for work interrupted by the app closing or crashing, and puts the job straight back in the queue on the next start.
- **AwaitingHandoff survives a restart.** The user can still complete the handoff on the source's website, so Requeue doesn't apply to it.
- **Verifying can't be paused.** Checking a hash is short and has no partial state worth keeping.
- **Failures are codes, not text.** `Fail` takes a `DownloadFailureReason`, which the UI turns into a localized message. Technical details belong in the log, which keeps URLs and keys out of the database.
- **The domain has no clock.** `DownloadJob.Create` takes the creation time as a value, and callers get it from `TimeProvider`.

Value objects (`SourceId`, `GameRef`, `ModRef`, `ModFileRef`, `FileHash`, `DownloadJobId`) are sealed record classes rather than record structs, so no instance can skip validation through `default`.

## Consequences

- `DownloadJobTransitionTests` checks every method against every status from an independent copy of the table above. Changing a transition means changing that table and this ADR together.
- The scheduler (Application) calls `Requeue` on jobs it finds in Resolving, Downloading or Verifying at startup, and on active jobs when the app shuts down.
- Adding a `DownloadFailureReason` or a status later is fine; renumbering the stored values is not.
