using ModShelf.Application.Library.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Common;
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
    private readonly ILibraryStorage _storage;

    public LinkDownloads(ISourceRegistry sources, IDownloadService downloads, ILibraryStorage storage)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(storage);

        _sources = sources;
        _downloads = downloads;
        _storage = storage;
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

        // A file saved there already isn't downloaded again: a new job would only fail on it.
        IReadOnlyList<DownloadJobSnapshot> jobs = await _downloads.ListAsync(cancellationToken).ConfigureAwait(false);
        DownloadJobSnapshot? saved = jobs.LastOrDefault(job =>
            job.Status == DownloadJobStatus.Completed
            && job.File == recognized.File
            && SavesTo(job, targetDirectory, name));
        if (saved is not null && _storage.FileExists(Path.Combine(targetDirectory, name)))
        {
            return new LinkDownload(saved, AlreadyQueued: false, AlreadySaved: true);
        }

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
            && SavesTo(job, targetDirectory, fileName));
        if (existing is not null)
        {
            return new LinkDownload(existing, AlreadyQueued: true);
        }

        DownloadJobSnapshot added = await downloads.EnqueueAsync(recognized.File, targetDirectory, fileName, cancellationToken)
            .ConfigureAwait(false);
        return new LinkDownload(added, AlreadyQueued: false);
    }

    /// <summary>
    /// Whether <paramref name="job"/> saves its file as <paramref name="fileName"/> in <paramref name="targetDirectory"/>,
    /// however the folder is written: with a trailing separator, or in another case where case doesn't count.
    /// </summary>
    private static bool SavesTo(DownloadJobSnapshot job, string targetDirectory, string fileName)
        => FilePaths.Same(Path.Combine(job.TargetDirectory, job.FileName), Path.Combine(targetDirectory, fileName));
}
