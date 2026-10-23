using System.Diagnostics;

using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Application.Tests.Common;
using ModShelf.Application.Tests.Fakes;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Testing;

namespace ModShelf.Application.Tests.Downloads;

/// <summary>
/// A running <see cref="DownloadScheduler"/> on fakes: two sources (Nexus and mod.io), a transfer the test
/// drives, an in-memory repository, a fake clock and a recording logger. It records every snapshot the
/// scheduler publishes.
/// </summary>
internal sealed class SchedulerHarness : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly List<DownloadJobSnapshot> _snapshots = [];
    private readonly CancellationTokenSource _stopWatching = new();
    private readonly Task _watching;
    private bool _disposed;

    private SchedulerHarness(DownloadSchedulerOptions options, IEnumerable<DownloadJob> storedJobs, bool registerModIo)
    {
        Repository = new InMemoryDownloadJobRepository(storedJobs);
        IModSource[] sources = registerModIo ? [Nexus, ModIo] : [Nexus];
        Scheduler = new DownloadScheduler(Repository, new SourceRegistry(sources), Transfer, Time, options, Logger);

        IAsyncEnumerable<DownloadJobSnapshot> stream = Scheduler.WatchAsync(_stopWatching.Token);
        _watching = Task.Run(async () =>
        {
            await foreach (DownloadJobSnapshot snapshot in stream)
            {
                lock (_gate)
                {
                    _snapshots.Add(snapshot);
                }
            }
        });
    }

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public InMemoryDownloadJobRepository Repository { get; }

    public ScriptedResolver Nexus { get; } = new(TestData.Nexus);

    public ScriptedResolver ModIo { get; } = new(TestData.ModIo);

    public ControlledFileTransfer Transfer { get; } = new();

    public RecordingLogger<DownloadScheduler> Logger { get; } = new();

    public DownloadScheduler Scheduler { get; }

    public static async Task<SchedulerHarness> StartAsync(
        DownloadSchedulerOptions? options = null,
        IEnumerable<DownloadJob>? storedJobs = null,
        bool registerModIo = true)
    {
        SchedulerHarness harness = new(options ?? new DownloadSchedulerOptions(), storedJobs ?? [], registerModIo);
        await harness.Scheduler.StartAsync(CancellationToken.None);
        return harness;
    }

    public Task<DownloadJobSnapshot> EnqueueAsync(SourceId source, string fileId)
        => Scheduler.EnqueueAsync(TestData.File(source, fileId), TestData.TargetDirectory, $"{fileId}.7z", CancellationToken.None);

    /// <summary>
    /// Waits until the scheduler has handled everything sent to it so far, and returns every job.
    /// </summary>
    public Task<IReadOnlyList<DownloadJobSnapshot>> SettleAsync()
        => Scheduler.ListAsync(CancellationToken.None).WaitAsync(SchedulerTimeouts.Wait);

    public async Task<DownloadJobSnapshot> CurrentAsync(DownloadJobId id)
        => Assert.Single(await SettleAsync(), job => job.Id == id);

    /// <summary>
    /// Waits for a snapshot of the job that matches <paramref name="condition"/>.
    /// </summary>
    public async Task<DownloadJobSnapshot> WaitForAsync(DownloadJobId id, Func<DownloadJobSnapshot, bool> condition)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (waited.Elapsed < SchedulerTimeouts.Wait)
        {
            lock (_gate)
            {
                DownloadJobSnapshot? match = _snapshots.Find(snapshot => snapshot.Id == id && condition(snapshot));
                if (match is not null)
                {
                    return match;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }

        throw new TimeoutException($"No matching snapshot of job {id}. Statuses seen: {string.Join(", ", Statuses(id))}.");
    }

    public Task<DownloadJobSnapshot> WaitForStatusAsync(DownloadJobId id, DownloadJobStatus status)
        => WaitForAsync(id, snapshot => snapshot.Status == status);

    /// <summary>
    /// Every status the job was published with, without repeats in a row.
    /// </summary>
    public IReadOnlyList<DownloadJobStatus> Statuses(DownloadJobId id)
    {
        lock (_gate)
        {
            List<DownloadJobStatus> statuses = [];
            foreach (DownloadJobSnapshot snapshot in _snapshots.Where(snapshot => snapshot.Id == id))
            {
                if (statuses.Count == 0 || statuses[^1] != snapshot.Status)
                {
                    statuses.Add(snapshot.Status);
                }
            }

            return statuses;
        }
    }

    /// <summary>
    /// Every snapshot of the job published while it was downloading.
    /// </summary>
    public IReadOnlyList<long> ProgressSnapshots(DownloadJobId id)
    {
        lock (_gate)
        {
            return
            [
                .. _snapshots
                    .Where(snapshot => snapshot.Id == id && snapshot.Status == DownloadJobStatus.Downloading)
                    .Select(snapshot => snapshot.BytesDownloaded),
            ];
        }
    }

    public async Task StopAsync()
    {
        await Scheduler.StopAsync(CancellationToken.None).WaitAsync(SchedulerTimeouts.Wait);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        Scheduler.Dispose();
        await _stopWatching.CancelAsync();
        await _watching.WaitAsync(SchedulerTimeouts.Wait);
        _stopWatching.Dispose();
    }
}
