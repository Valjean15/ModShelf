using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;
using ModShelf.Domain.Downloads;

namespace ModShelf.Application.Library;

/// <summary>
/// Records downloads into the library as they finish, while the download engine runs; and, at start, the ones that
/// finished while nothing recorded them.
/// </summary>
internal sealed partial class LibraryRecorder : BackgroundService
{
    private readonly ILibraryDownloads _library;
    private readonly IDownloadService _downloads;
    private readonly ILogger<LibraryRecorder> _logger;

    public LibraryRecorder(ILibraryDownloads library, IDownloadService downloads, ILogger<LibraryRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(logger);

        _library = library;
        _downloads = downloads;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Watched first, so a download that finishes while the missed ones are recorded isn't missed too.
        IAsyncEnumerable<DownloadJobSnapshot> updates = _downloads.WatchAsync(stoppingToken);
        try
        {
            await _library.RecordMissedAsync(stoppingToken).ConfigureAwait(false);

            // The stream ends when the host stops, or when the download engine does.
            await foreach (DownloadJobSnapshot job in updates.ConfigureAwait(false))
            {
                if (job.Status is not (DownloadJobStatus.Completed or DownloadJobStatus.Cancelled))
                {
                    continue;
                }

                try
                {
                    await _library.RecordAsync(job, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogCantRecord(_logger, exception, job.FileName);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }
}
