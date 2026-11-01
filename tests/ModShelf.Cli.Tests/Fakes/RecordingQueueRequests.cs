using System.Collections.Concurrent;

using ModShelf.Application.Downloads;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A queue's requests, recorded, each done.
/// </summary>
internal sealed class RecordingQueueRequests : IQueueRequests
{
    private readonly ConcurrentQueue<QueueRequest> _requests = new();

    public IReadOnlyList<QueueRequest> Requests => [.. _requests];

    public Task<QueueReply> HandleAsync(QueueRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(new QueueReply(QueueOutcome.Done));
    }
}
