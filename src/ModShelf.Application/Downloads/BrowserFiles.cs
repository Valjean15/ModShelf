using System.Text.RegularExpressions;
using System.Threading.Channels;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Downloads;

/// <summary>
/// Watches the folder the browser saves downloads to while a download waits on a site's page, and hands that download
/// a file the browser saves there that's its (ADR 0091): one with the SHA-256 the site gives for its file, or with its
/// file's name, in any case, with or without the number a browser adds to a name it has already, such as " (1)". Only
/// files that appear once the download is waiting count; a file several downloads wait for goes to the one that started
/// waiting last, the one the user most likely just started on its page. Runs where the download queue runs.
/// </summary>
internal sealed partial class BrowserFiles(
    IDownloadService downloads,
    IBrowserDownloads browser,
    IUserFolders folders,
    ISettingsService settings,
    ISourceRegistry sources,
    ILibraryStorage storage,
    TimeProvider timeProvider,
    ILogger<BrowserFiles> logger) : BackgroundService
{
    private readonly Channel<object> _events = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });

    // Owned by the loop.
    private readonly Dictionary<DownloadJobId, Waiting> _waiting = [];
    private CancellationTokenSource? _watch;
    private long _turns;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await downloads.WhenQueueRunsHereAsync(stoppingToken).ConfigureAwait(false);
            _ = PumpJobsAsync(stoppingToken);
            await foreach (object happened in _events.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await HandleAsync(happened, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            StopWatching();
        }
    }

    /// <summary>
    /// Whether the browser's name for a file is <paramref name="fileName"/>: the same in any case, or with the number a
    /// browser adds to a name its folder has already, such as <c>mod (1).zip</c> or <c>mod(2).zip</c>.
    /// </summary>
    internal static bool SameName(string fileName, string saved)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(saved);

        if (string.Equals(fileName, saved, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Match numbered = Numbered().Match(saved);
        return numbered.Success
            && string.Equals(fileName, numbered.Groups["name"].Value + numbered.Groups["extension"].Value, StringComparison.OrdinalIgnoreCase);
    }

    private async Task PumpJobsAsync(CancellationToken stoppingToken)
    {
        try
        {
            IAsyncEnumerable<DownloadJobSnapshot> changes = downloads.WatchAsync(stoppingToken);
            foreach (DownloadJobSnapshot job in await downloads.ListAsync(stoppingToken).ConfigureAwait(false))
            {
                _events.Writer.TryWrite(job);
            }

            await foreach (DownloadJobSnapshot job in changes.ConfigureAwait(false))
            {
                _events.Writer.TryWrite(job);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException)
        {
            // The host is stopping, or the queue stopped.
        }
    }

    private async Task HandleAsync(object happened, CancellationToken stoppingToken)
    {
        switch (happened)
        {
            case DownloadJobSnapshot job:
                Track(job, stoppingToken);
                break;
            case HashKnown known when _waiting.TryGetValue(known.Id, out Waiting? waiting):
                _waiting[known.Id] = waiting with { Sha256 = known.Sha256 };
                break;
            case FileAppeared file:
                await TakeAsync(file, stoppingToken).ConfigureAwait(false);
                break;
        }

        if (_waiting.Count > 0 && _watch is null)
        {
            await StartWatchingAsync(stoppingToken).ConfigureAwait(false);
        }
        else if (_waiting.Count == 0)
        {
            StopWatching();
        }
    }

    /// <summary>
    /// Notes a download that started waiting on a page, asking its site for its file's SHA-256, and forgets one that
    /// stopped.
    /// </summary>
    private void Track(DownloadJobSnapshot job, CancellationToken stoppingToken)
    {
        if (job.Status != DownloadJobStatus.AwaitingHandoff)
        {
            _waiting.Remove(job.Id);
            return;
        }

        if (_waiting.ContainsKey(job.Id))
        {
            return;
        }

        _waiting[job.Id] = new Waiting(job.FileName, timeProvider.GetUtcNow(), ++_turns, Sha256: null);
        _ = AskHashAsync(job.Id, job.File, stoppingToken);
    }

    private async Task AskHashAsync(DownloadJobId id, ModFileRef file, CancellationToken stoppingToken)
    {
        IReadOnlyList<FileHash> hashes = await SiteFileHashes.OfAsync(sources, file, stoppingToken).ConfigureAwait(false);
        if (hashes.FirstOrDefault(hash => hash.Algorithm == FileHashAlgorithm.Sha256) is { } sha256)
        {
            _events.Writer.TryWrite(new HashKnown(id, sha256.Value));
        }
    }

    private async Task StartWatchingAsync(CancellationToken stoppingToken)
    {
        string folder = (await settings.GetAsync(stoppingToken).ConfigureAwait(false)).BrowserDownloadsFolder ?? folders.Downloads;
        _watch = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _ = PumpFilesAsync(folder, _watch.Token);
        LogWatching(logger, _waiting.Count);
    }

    private void StopWatching()
    {
        if (_watch is not null)
        {
            _watch.Cancel();
            _watch.Dispose();
            _watch = null;
        }
    }

    private async Task PumpFilesAsync(string folder, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string path in browser.WatchAsync(folder, cancellationToken).ConfigureAwait(false))
            {
                _events.Writer.TryWrite(new FileAppeared(path, timeProvider.GetUtcNow()));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogCantWatch(logger, exception);
        }
    }

    /// <summary>
    /// Hands the file to the download waiting for it, if one is: by its name, else by its SHA-256.
    /// </summary>
    private async Task TakeAsync(FileAppeared file, CancellationToken stoppingToken)
    {
        string name = Path.GetFileName(file.Path);
        List<KeyValuePair<DownloadJobId, Waiting>> before =
            [.. _waiting.Where(waiting => waiting.Value.Since <= file.At).OrderByDescending(waiting => waiting.Value.Turn)];
        DownloadJobId? match = before.Where(waiting => SameName(waiting.Value.FileName, name)).Select(waiting => (DownloadJobId?)waiting.Key).FirstOrDefault();
        if (match is null && before.Exists(waiting => waiting.Value.Sha256 is not null))
        {
            // Both in lowercase: FileHash keeps its digest so, and the storage gives it so.
            string? sha256 = await Sha256Async(file.Path, stoppingToken).ConfigureAwait(false);
            match = before
                .Where(waiting => sha256 is not null && string.Equals(waiting.Value.Sha256, sha256, StringComparison.Ordinal))
                .Select(waiting => (DownloadJobId?)waiting.Key)
                .FirstOrDefault();
        }

        if (match is not { } id)
        {
            return;
        }

        try
        {
            TakeFileOutcome outcome = await downloads.TakeFileAsync(id, file.Path, stoppingToken).ConfigureAwait(false);
            LogTook(logger, name, id, outcome);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or DownloadQueueBusyException)
        {
            // It stopped waiting meanwhile.
        }
    }

    private async Task<string?> Sha256Async(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await storage.Sha256Async(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^(?<name>.+?) ?\(\d+\)(?<extension>\.[^.]*)?$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Numbered();

    /// <summary>
    /// A download waiting on a page: its file's name, since when, its turn among the downloads that started waiting, and
    /// its SHA-256 once its site said.
    /// </summary>
    private sealed record Waiting(string FileName, DateTimeOffset Since, long Turn, string? Sha256);

    private sealed record HashKnown(DownloadJobId Id, string Sha256);

    private sealed record FileAppeared(string Path, DateTimeOffset At);
}
