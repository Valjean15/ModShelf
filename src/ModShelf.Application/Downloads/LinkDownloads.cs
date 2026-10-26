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

        // Before the job is found or added, so that the job resolves with the link from its first attempt.
        if (recognized.IsHandoff)
        {
            await _downloads.HandOffAsync(recognized.File, link, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<DownloadJobSnapshot> jobs = await _downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        DownloadJobSnapshot? existing = jobs.FirstOrDefault(job =>
            job.Status is not (DownloadJobStatus.Completed or DownloadJobStatus.Cancelled)
            && job.File == recognized.File
            && string.Equals(job.TargetDirectory, targetDirectory, StringComparison.Ordinal)
            && string.Equals(job.FileName, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return new LinkDownload(existing, AlreadyQueued: true);
        }

        DownloadJobSnapshot added = await _downloads.EnqueueAsync(recognized.File, targetDirectory, name, cancellationToken)
            .ConfigureAwait(false);
        return new LinkDownload(added, AlreadyQueued: false);
    }
}
