using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// The messages the scheduler's loop handles, and the bookkeeping for running jobs.
/// </summary>
internal sealed partial class DownloadScheduler
{
    private enum JobControl
    {
        Pause,
        Resume,
        Retry,
        Cancel,
    }

    /// <summary>
    /// Why a running download was asked to stop. A later, stronger request wins: Cancel over Pause over Shutdown.
    /// </summary>
    private enum StopReason
    {
        Shutdown,
        Pause,
        Cancel,
    }

    private enum AttemptEnd
    {
        Completed,
        HandoffRequired,
        Failed,
        Stopped,
    }

    /// <param name="HandoffPage">For <see cref="AttemptEnd.HandoffRequired"/>: the page where the user starts the download.</param>
    private readonly record struct AttemptResult(AttemptEnd End, DownloadFailureReason? FailureReason = null, Uri? HandoffPage = null)
    {
        public static AttemptResult Completed => new(AttemptEnd.Completed);

        public static AttemptResult Stopped => new(AttemptEnd.Stopped);

        public static AttemptResult Failed(DownloadFailureReason reason) => new(AttemptEnd.Failed, reason);

        public static AttemptResult HandoffRequired(Uri page) => new(AttemptEnd.HandoffRequired, HandoffPage: page);
    }

    private abstract record Message;

    /// <summary>
    /// A request from a caller, answered through its reply.
    /// </summary>
    private abstract record Command : Message
    {
        public abstract void Reject(Exception exception);
    }

    private sealed record EnqueueCommand(DownloadJob Job, TaskCompletionSource<DownloadJobSnapshot> Reply) : Command
    {
        public override void Reject(Exception exception) => Reply.TrySetException(exception);
    }

    private sealed record ListCommand(TaskCompletionSource<IReadOnlyList<DownloadJobSnapshot>> Reply) : Command
    {
        public override void Reject(Exception exception) => Reply.TrySetException(exception);
    }

    private sealed record ControlCommand(DownloadJobId Id, JobControl Control, TaskCompletionSource Reply) : Command
    {
        public override void Reject(Exception exception) => Reply.TrySetException(exception);
    }

    /// <param name="Link">Secret: it can carry a key, so it's left out of <see cref="ToString"/>.</param>
    private sealed record HandOffCommand(ModFileRef File, Uri Link, TaskCompletionSource Reply) : Command
    {
        public override void Reject(Exception exception) => Reply.TrySetException(exception);

        public override string ToString() => $"{nameof(HandOffCommand)} {{ File = {File} }}";
    }

    /// <summary>
    /// News from a running download about its job.
    /// </summary>
    private abstract record Report(DownloadJobId Id) : Message;

    private sealed record TransferStarted(DownloadJobId Id, long ResumeFromByte, long? TotalBytes) : Report(Id);

    private sealed record TransferAdvanced(DownloadJobId Id, long BytesDownloaded) : Report(Id);

    private sealed record TransferVerifying(DownloadJobId Id) : Report(Id);

    private sealed record LinkExpired(DownloadJobId Id) : Report(Id);

    private sealed record AttemptFinished(DownloadJobId Id, AttemptResult Result) : Report(Id);

    /// <param name="handoff">The handoff link the attempt resolves with, if any.</param>
    private sealed class RunningJob(SourceId source, Uri? handoff) : IDisposable
    {
        private readonly List<TaskCompletionSource> _waiters = [];

        public SourceId Source { get; } = source;

        public Uri? Handoff { get; } = handoff;

        public CancellationTokenSource Cancellation { get; } = new();

        public StopReason? StopReason { get; private set; }

        public void RequestStop(StopReason reason, TaskCompletionSource? waiter)
        {
            if (StopReason is null || reason > StopReason.Value)
            {
                StopReason = reason;
            }

            if (waiter is not null)
            {
                _waiters.Add(waiter);
            }

            Cancellation.Cancel();
        }

        public void CompleteWaiters(Exception? error)
        {
            foreach (TaskCompletionSource waiter in _waiters)
            {
                if (error is null)
                {
                    waiter.TrySetResult();
                }
                else
                {
                    waiter.TrySetException(error);
                }
            }
        }

        public void Dispose() => Cancellation.Dispose();
    }
}
