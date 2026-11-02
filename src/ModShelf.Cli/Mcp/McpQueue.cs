using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library;
using ModShelf.Domain.Downloads;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The library's download queue, as the MCP server changes it (ADR 0040). The desktop app runs the queue when it runs,
/// so a request goes to it. When none runs, this process runs the queue, in a host of its own, until no download is under
/// way: one waiting for the user to start it on the site isn't, so the desktop app, which the site's link starts, can
/// take the queue over. It stops once the finished downloads are in the library, too.
/// </summary>
/// <remarks>
/// One request at a time. Disposing stops this process's queue: a download under way stops where it is, and goes on the
/// next time the queue runs, as when the CLI's download stops.
/// </remarks>
internal sealed partial class McpQueue : IAsyncDisposable
{
    private readonly IQueueOwner _owner;
    private readonly Func<IHost> _buildRunner;
    private readonly TimeSpan _checkEvery;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IHost? _runner;
    private CancellationTokenSource? _watching;
    private Task _watcher = Task.CompletedTask;
    private bool _disposed;
    private int _disposing;

    /// <param name="owner">The desktop app, when it runs.</param>
    /// <param name="buildRunner">Builds the host that runs the queue here, with its downloads.</param>
    /// <param name="checkEvery">How often to look whether a download is under way here.</param>
    public McpQueue(IQueueOwner owner, Func<IHost> buildRunner, TimeSpan checkEvery, TimeProvider time, ILogger<McpQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(buildRunner);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(checkEvery, TimeSpan.Zero);

        _owner = owner;
        _buildRunner = buildRunner;
        _checkEvery = checkEvery;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Whether this process runs the queue now.
    /// </summary>
    public bool RunsHere => Volatile.Read(ref _runner) is not null;

    /// <summary>
    /// Carries out <paramref name="request"/>: in the desktop app when it runs, otherwise here.
    /// </summary>
    /// <returns>Its reply, and whether this process carried it out.</returns>
    /// <exception cref="TimeoutException">The desktop app took it, but didn't reply in time: it may have carried it out.</exception>
    /// <exception cref="InvalidDataException">The desktop app's reply couldn't be read, as from a newer app.</exception>
    public async Task<(QueueReply Reply, bool RanHere)> SendAsync(QueueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runner is null)
            {
                if (await _owner.SendAsync(request, cancellationToken).ConfigureAwait(false) is { } reply)
                {
                    return (reply, false);
                }

                if (await StartRunnerAsync(cancellationToken).ConfigureAwait(false) is { } refused)
                {
                    return (refused, false);
                }
            }

            QueueReply here = await _runner!.Services.GetRequiredService<IQueueRequests>().HandleAsync(request, cancellationToken).ConfigureAwait(false);
            return (here, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The queue as this process runs it, with the pages where the user starts the downloads that wait for them.
    /// </summary>
    /// <returns>The downloads, the oldest first; null when this process doesn't run the queue.</returns>
    public async Task<IReadOnlyList<DownloadJobSnapshot>?> ListHereAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _runner is null ? null : await _runner.Services.GetRequiredService<IDownloadService>().ListAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) == 1)
        {
            return;
        }

        CancellationTokenSource? watching;
        Task watcher;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            watching = _watching;
            watcher = _watcher;
        }
        finally
        {
            _gate.Release();
        }

        if (watching is not null)
        {
            await watching.CancelAsync().ConfigureAwait(false);
        }

        await watcher.ConfigureAwait(false);
        if (Interlocked.Exchange(ref _runner, null) is { } runner)
        {
            await StopAsync(runner).ConfigureAwait(false);
        }

        watching?.Dispose();
        _gate.Dispose();
    }

    private static bool IsUnderWay(DownloadJobSnapshot job)
        => job.Status is DownloadJobStatus.Queued or DownloadJobStatus.Resolving or DownloadJobStatus.Downloading or DownloadJobStatus.Verifying;

    /// <summary>
    /// Runs the queue here. Only while the gate is held.
    /// </summary>
    /// <returns>Null once it runs here; the refusal when another process runs it.</returns>
    private async Task<QueueReply?> StartRunnerAsync(CancellationToken cancellationToken)
    {
        IHost runner = _buildRunner();
        try
        {
            await runner.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            runner.Dispose();
            throw;
        }

        if (runner.Services.GetRequiredService<IDownloadService>().IsQueueBusyElsewhere)
        {
            // Such as the CLI's download command, or a desktop app from before the queue's pipe.
            LogBusyElsewhere(_logger);
            await StopAsync(runner).ConfigureAwait(false);
            return new QueueReply(QueueOutcome.Refused, McpProblems.QueueBusyElsewhere);
        }

        LogRunsHere(_logger);
        _watching?.Dispose();
        _watching = new CancellationTokenSource();
        Volatile.Write(ref _runner, runner);
        _watcher = WatchAsync(runner, _watching.Token);
        return null;
    }

    /// <summary>
    /// Stops the queue here once no download is under way, and the ones finished are in the library.
    /// </summary>
    private async Task WatchAsync(IHost runner, CancellationToken cancellationToken)
    {
        IDownloadService downloads = runner.Services.GetRequiredService<IDownloadService>();
        ILibraryDownloads library = runner.Services.GetRequiredService<ILibraryDownloads>();
        using PeriodicTimer timer = new(_checkEvery, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    IReadOnlyList<DownloadJobSnapshot> jobs;
                    try
                    {
                        jobs = await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // Such as a database busy a moment: the queue keeps running here, and the next look tries again.
                        LogCantLook(_logger, exception);
                        continue;
                    }

                    if (jobs.Any(IsUnderWay))
                    {
                        continue;
                    }

                    // A download that just finished may not be in the library yet, and stopping the host would stop its
                    // recording: this waits for one being recorded, and records one not reached yet.
                    await library.RecordMissedAsync(cancellationToken).ConfigureAwait(false);
                    LogDone(_logger);
                    Volatile.Write(ref _runner, null);
                    await StopAsync(runner).ConfigureAwait(false);
                    return;
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposing stops the queue here.
        }
    }

    private async Task StopAsync(IHost runner)
    {
        try
        {
            await runner.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogCantStop(_logger, exception);
        }
        finally
        {
            runner.Dispose();
        }
    }
}
