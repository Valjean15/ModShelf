using System.Net;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Downloads;

/// <summary>
/// One download attempt: resolve a ticket, transfer the file, and refresh the ticket when it expires.
/// It runs outside the scheduler's loop and only reports back to it; it never touches the job itself, and never
/// logs the handoff link it resolves with.
/// </summary>
internal sealed partial class DownloadScheduler
{
    private void Start(DownloadJob job, IDownloadResolver resolver)
    {
        RunningJob running = new(job.File.Mod.Game.Source, _handoffs.GetValueOrDefault(job.File));
        _running.Add(job.Id, running);

        DownloadJobId id = job.Id;
        ModFileRef file = job.File;
        Uri? handoff = running.Handoff;
        string destination = DestinationOf(job);
        CancellationToken cancellationToken = running.Cancellation.Token;

        // Not awaited on purpose: the attempt never throws, and reports its end through the inbox.
        _ = Task.Run(() => RunAttemptAsync(id, file, handoff, destination, resolver, cancellationToken), CancellationToken.None);
    }

    private async Task RunAttemptAsync(
        DownloadJobId id,
        ModFileRef file,
        Uri? handoff,
        string destination,
        IDownloadResolver resolver,
        CancellationToken cancellationToken)
    {
        AttemptResult result;
        try
        {
            result = await AttemptAsync(id, file, handoff, destination, resolver, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = AttemptResult.Stopped;
        }
        catch (Exception exception)
        {
            LogAttemptCrashed(_logger, exception, id);
            result = AttemptResult.Failed(DownloadFailureReason.Unexpected);
        }

        Send(new AttemptFinished(id, result));
    }

    /// <param name="handoff">Passed on every resolve, including after a ticket expires, while the source takes it.</param>
    private async Task<AttemptResult> AttemptAsync(
        DownloadJobId id,
        ModFileRef file,
        Uri? handoff,
        string destination,
        IDownloadResolver resolver,
        CancellationToken cancellationToken)
    {
        TransferProgress progress = new(this, id);
        for (int refreshes = 0; ; refreshes++)
        {
            ResolveResult resolved;
            try
            {
                resolved = await resolver.ResolveAsync(file, handoff, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (ResolveFailure(exception) is { } reason)
            {
                LogResolveFailed(_logger, exception, id, resolver.Id);
                return AttemptResult.Failed(reason);
            }

            if (resolved is HandoffRequired required)
            {
                return AttemptResult.HandoffRequired(required.PageUrl);
            }

            if (resolved is not DownloadTicket ticket)
            {
                throw new InvalidOperationException($"Source {resolver.Id} returned no download ticket.");
            }

            TransferOutcome outcome = await _transfer
                .DownloadAsync(ticket, destination, progress, cancellationToken)
                .ConfigureAwait(false);

            switch (outcome)
            {
                case TransferOutcome.Completed:
                    return AttemptResult.Completed;
                case TransferOutcome.LinkExpired when refreshes < _options.MaxLinkRefreshes:
                    Send(new LinkExpired(id));
                    continue;
                case TransferOutcome.LinkExpired:
                    return AttemptResult.Failed(DownloadFailureReason.LinkUnavailable);
                case TransferOutcome.HashMismatch:
                    return AttemptResult.Failed(DownloadFailureReason.HashMismatch);
                case TransferOutcome.NetworkError:
                    return AttemptResult.Failed(DownloadFailureReason.Network);
                case TransferOutcome.StorageError:
                    return AttemptResult.Failed(DownloadFailureReason.Storage);
                default:
                    throw new InvalidOperationException($"Unknown transfer outcome {outcome}.");
            }
        }
    }

    /// <summary>
    /// Why resolving failed, for the failures a source reports in the shared way: a refused account, the file
    /// gone, or the site out of reach. Null for anything else, which fails the job as unexpected.
    /// </summary>
    private static DownloadFailureReason? ResolveFailure(Exception exception) => exception switch
    {
        AuthenticationFailedException => DownloadFailureReason.AccountNeeded,
        HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone } => DownloadFailureReason.LinkUnavailable,
        HttpRequestException => DownloadFailureReason.Network,
        _ => null,
    };

    /// <summary>
    /// Hands a report to the loop. After the scheduler has stopped there is nobody left to tell.
    /// </summary>
    private void Send(Report report) => _inbox.Writer.TryWrite(report);

    /// <summary>
    /// Forwards what the transfer reports to the scheduler's loop.
    /// </summary>
    private sealed class TransferProgress(DownloadScheduler scheduler, DownloadJobId id) : ITransferProgress
    {
        public void Started(long resumeFromByte, long? totalBytes)
            => scheduler.Send(new TransferStarted(id, resumeFromByte, totalBytes));

        public void Advanced(long bytesDownloaded)
            => scheduler.Send(new TransferAdvanced(id, bytesDownloaded));

        public void Verifying()
            => scheduler.Send(new TransferVerifying(id));
    }
}
