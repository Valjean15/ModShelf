using ModShelf.Application.Downloads.Ports;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// One call to <see cref="ControlledFileTransfer.DownloadAsync"/>. The test reports progress through it and
/// decides how it ends. Cancelling the call's token ends it the way a real transfer would.
/// </summary>
internal sealed class TransferCall
{
    private readonly TaskCompletionSource<TransferOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TransferCall(DownloadTicket ticket, string destinationPath, ITransferProgress progress, CancellationToken cancellationToken)
    {
        Ticket = ticket;
        DestinationPath = destinationPath;
        Progress = progress;
        CancellationToken = cancellationToken;
        cancellationToken.Register(() => _outcome.TrySetCanceled(cancellationToken));
    }

    public DownloadTicket Ticket { get; }

    public string DestinationPath { get; }

    public ITransferProgress Progress { get; }

    public CancellationToken CancellationToken { get; }

    public Task<TransferOutcome> Outcome => _outcome.Task;

    public void End(TransferOutcome outcome) => _outcome.TrySetResult(outcome);

    public void Throw(Exception exception) => _outcome.TrySetException(exception);

    /// <summary>
    /// Reports a whole download of <paramref name="totalBytes"/>, then completes.
    /// </summary>
    public void Succeed(long totalBytes = 100)
    {
        Progress.Started(0, totalBytes);
        Progress.Advanced(totalBytes);
        Progress.Verifying();
        End(TransferOutcome.Completed);
    }
}
