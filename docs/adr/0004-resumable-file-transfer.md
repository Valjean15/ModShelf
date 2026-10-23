# 4. Resumable HTTP file transfer

- Status: Accepted
- Date: 2026-10-23

## Context

Mod files range from a few kilobytes to many gigabytes. Downloads get interrupted by dropped connections, stalled servers, sleep, app restarts and crashes. Download links from mod sites expire, often within hours, so a restarted download usually needs a new link for the same file. Some servers ignore `Range` requests or answer with a different range than asked for. A file must never be half-written at its final path, and an existing file must never be overwritten.

## Decision

`HttpFileTransfer` (in `ModShelf.Infrastructure`) implements `IFileTransfer`. It downloads one `DownloadTicket` to a destination path and reports a `TransferOutcome`. It does not retry with a new link itself; the scheduler resolves a new ticket when the outcome is `LinkExpired`.

- **Bytes go to `<destination>.part`.** The file stream is unbuffered, so the partial file's length always equals the bytes received, even after a crash. The partial file is the only resume state; nothing about it is saved in the database.
- **Resuming sends `Range: bytes=<length>-`.** The response decides what happens next:
  - `206` starting at that offset: append to the partial file.
  - `200`: the server ignored the range, so start over from byte 0.
  - `206` at any other offset: delete the partial file and ask again from 0.
  - `416` whose `Content-Range` length equals the partial file's length: the file is already complete.
  - Any other `416`: the partial file is longer than the server's file, so delete it and start over.
  - `401`, `403`, `404` or `410`: `LinkExpired`. The partial file is kept so that the new link can resume it.
  - Any other error status: `NetworkError`, after the retries below.
- **Two layers of retry.** The `HttpClient` resilience pipeline retries a request that fails before the body starts (connection errors, `408`, `429` and `5xx`), `MaxRetryAttempts` times. Once the body is streaming, a dropped connection, a read that stalls for `ReadTimeout`, or a body that ends early is resumed with a new `Range` request, up to `MaxResumeAttempts` times per transfer. After that the outcome is `NetworkError` and the partial file is kept. The pipeline holds only a retry: the standard resilience handler's request timeouts and circuit breaker are built for short API calls and would cut off long downloads, so the read-idle timeout does that job instead.
- **Verify, then rename in one step.** When the ticket has an expected hash (MD5, SHA-1, SHA-256 or SHA-512), the partial file is hashed first. A mismatch deletes the partial file and returns `HashMismatch`, because resuming a corrupt file would only produce the same result. MD5 and SHA-1 are accepted only because some sites publish nothing stronger; they check integrity, not authenticity. A matching file is moved to the destination with `File.Move(..., overwrite: false)`.
- **Never overwrite.** If the destination exists before the transfer starts, or appears before the final move, the outcome is `StorageError` and the existing file is untouched. Any other file system failure (no permission, disk full, a file where a folder should be) is also `StorageError`.
- **Cancellation throws `OperationCanceledException` and keeps the partial file**, so pause and app shutdown can resume later. Deleting the partial file is a separate call (`DeletePartialFileAsync`) that the scheduler makes when a job is cancelled.
- **Progress is throttled** to at most one `Advanced` report per `ProgressInterval`, plus a final report when the bytes stop.
- **Logs name the host and the file name only.** The URL, its query string and the ticket's headers can carry keys, so they are never logged.

Every request carries `User-Agent: ModShelf/<version>`, and `AddHttpFileTransfer` registers the transfer with its named `HttpClient` and resilience pipeline.

## Consequences

- A download survives a crash or restart with at most the in-flight buffer lost, and a resumed download costs only the missing bytes on servers that support ranges.
- Servers that ignore ranges still work, but resuming restarts the download from 0.
- A file can only be verified when the source supplies a hash. Without one, a server that silently sends different content on resume could produce a corrupt file; the length check catches truncation but not that.
- Tests use a scripted `HttpMessageHandler` and scripted response bodies (drop, stall, early end) in place of a real server, plus a fake clock for the progress throttle. A separate test class runs the real DI registration to check the retry pipeline and the User-Agent.
