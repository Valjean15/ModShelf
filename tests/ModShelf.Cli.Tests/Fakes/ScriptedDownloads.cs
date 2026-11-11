using System.Collections.Concurrent;

using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A download queue that lists what the test says, and says whether another process runs it; nothing else.
/// </summary>
internal sealed class ScriptedDownloads : IDownloadService
{
    private readonly ConcurrentQueue<DownloadJobSnapshot> _jobs = new();

    public bool IsQueueBusyElsewhere { get; set; }

    /// <summary>
    /// The downloads it lists, which the test can change while it runs.
    /// </summary>
    public IReadOnlyList<DownloadJobSnapshot> Jobs { get; set; } = [];

    public Task WhenQueueRunsHereAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<DownloadJobSnapshot> EnqueueAsync(ModFileRef file, string targetDirectory, string fileName, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task HandOffAsync(ModFileRef file, Uri handoff, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<DownloadJobSnapshot>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(Jobs);

    public Task PauseAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task ResumeAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task RetryAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task CancelAsync(DownloadJobId id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<TakeFileOutcome> TakeFileAsync(DownloadJobId id, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> ClearFinishedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public IAsyncEnumerable<DownloadJobSnapshot> WatchAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
