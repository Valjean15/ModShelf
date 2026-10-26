using System.Collections.Concurrent;
using System.Threading.Channels;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Tests.Common;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A file transfer the test drives by hand: each download waits until the test ends it.
/// </summary>
internal sealed class ControlledFileTransfer : IFileTransfer
{
    private readonly Channel<TransferCall> _calls = Channel.CreateUnbounded<TransferCall>();
    private int _callCount;

    public ConcurrentQueue<string> DeletedPartialFiles { get; } = new();

    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>
    /// What deleting a partial file throws, if anything, such as a file another program holds open.
    /// </summary>
    public Exception? DeleteFailure { get; set; }

    public Task<TransferOutcome> DownloadAsync(
        DownloadTicket ticket,
        string destinationPath,
        ITransferProgress progress,
        CancellationToken cancellationToken)
    {
        TransferCall call = new(ticket, destinationPath, progress, cancellationToken);
        Interlocked.Increment(ref _callCount);
        _calls.Writer.TryWrite(call);
        return call.Outcome;
    }

    public Task DeletePartialFileAsync(string destinationPath, CancellationToken cancellationToken)
    {
        if (DeleteFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        DeletedPartialFiles.Enqueue(destinationPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The next download the scheduler starts. Fails the test if none starts within a few seconds.
    /// </summary>
    public async Task<TransferCall> NextCallAsync()
        => await _calls.Reader.ReadAsync().AsTask().WaitAsync(SchedulerTimeouts.Wait);
}
