using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Downloads;

/// <summary>
/// Asks every <see cref="ILinkRecognizer"/> about a link and queues the file through <see cref="IDownloadService"/>.
/// A link that completes a handoff is handed to the queue too.
/// </summary>
/// <remarks>
/// The check for an existing job and the enqueue are two steps, so two callers adding the same link at the
/// same moment can still both add it. The CLI and a single desktop window never do.
/// </remarks>
internal sealed class LinkDownloads : ILinkDownloads
{
    private readonly ISourceRegistry _sources;
    private readonly IDownloadService _downloads;

    public LinkDownloads(ISourceRegistry sources, IDownloadService downloads)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(downloads);

        _sources = sources;
        _downloads = downloads;
    }

    public async Task<LinkDownload> AddAsync(Uri link, string targetDirectory, string? fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        if (!link.IsAbsoluteUri)
        {
            throw new ArgumentException("The link must be absolute.", nameof(link));
        }

        RecognizedLink recognized = await _sources.RecognizeAsync(link, cancellationToken).ConfigureAwait(false);
        string name = fileName
            ?? recognized.SuggestedFileName
            ?? throw new ArgumentException("The link doesn't say what the file is called, so a file name is needed.", nameof(fileName));

        return await AddRecognizedAsync(_downloads, recognized, link, targetDirectory, name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues the file of a link already recognized, as <see cref="AddAsync"/> does once it knows where the file goes.
    /// </summary>
    internal static async Task<LinkDownload> AddRecognizedAsync(
        IDownloadService downloads,
        RecognizedLink recognized,
        Uri link,
        string targetDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Before the job is found or added, so that the job resolves with the link from its first attempt.
        if (recognized.IsHandoff)
        {
            await downloads.HandOffAsync(recognized.File, link, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<DownloadJobSnapshot> jobs = await downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        DownloadJobSnapshot? existing = jobs.FirstOrDefault(job =>
            job.Status is not (DownloadJobStatus.Completed or DownloadJobStatus.Cancelled)
            && job.File == recognized.File
            && string.Equals(job.TargetDirectory, targetDirectory, StringComparison.Ordinal)
            && string.Equals(job.FileName, fileName, StringComparison.Ordinal));
        if (existing is not null)
        {
            return new LinkDownload(existing, AlreadyQueued: true);
        }

        DownloadJobSnapshot added = await downloads.EnqueueAsync(recognized.File, targetDirectory, fileName, cancellationToken)
            .ConfigureAwait(false);
        return new LinkDownload(added, AlreadyQueued: false);
    }
}
