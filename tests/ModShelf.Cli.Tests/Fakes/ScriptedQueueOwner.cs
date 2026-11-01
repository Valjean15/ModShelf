using System.Collections.Concurrent;

using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A desktop app that runs the library's downloads, as the MCP server reaches it: it records each request, and replies
/// as the test says, done by default.
/// </summary>
internal sealed class ScriptedQueueOwner : IQueueOwner
{
    private readonly ConcurrentQueue<QueueRequest> _requests = new();

    /// <summary>
    /// Whether the desktop app runs, so the test's app asks it.
    /// </summary>
    public bool Runs { get; set; }

    /// <summary>
    /// Its reply; null as when no desktop app runs.
    /// </summary>
    public QueueReply? Reply { get; set; } = new(QueueOutcome.Done);

    /// <summary>
    /// What asking it throws, in place of a reply; null for none.
    /// </summary>
    public Exception? Fails { get; set; }

    public IReadOnlyList<QueueRequest> Requests => [.. _requests];

    public Task<QueueReply?> SendAsync(QueueRequest request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Fails is { } failure ? Task.FromException<QueueReply?>(failure) : Task.FromResult<QueueReply?>(Reply);
    }
}
