using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Links;

/// <summary>
/// Hands a link to the unfinished download of its file outside the library, or puts the file into the library.
/// </summary>
internal sealed partial class OpenedLinks(
    ISourceRegistry sources,
    IDownloadService downloads,
    ILinkDownloads links,
    ILibraryDownloads library,
    ISettingsService settings,
    ILogger<OpenedLinks> logger) : IOpenedLinks
{
    public async Task<OpenedLink> OpenAsync(Uri link, CancellationToken cancellationToken)
    {
        RecognizedLink recognized = await sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false);
        string root = await settings.GetLibraryRootAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DownloadJobSnapshot> jobs = await downloads.ListAsync(cancellationToken).ConfigureAwait(false);

        // Such as a download to a folder, waiting for the user to start it on the website: it goes on with the link,
        // where it was going, rather than the file being downloaded a second time into the library.
        if (jobs.LastOrDefault(job => job.File == recognized.File && !IsDone(job) && !IsInLibrary(root, job)) is { } elsewhere)
        {
            LinkDownload going = await links.AddAsync(link, elsewhere.TargetDirectory, elsewhere.FileName, cancellationToken).ConfigureAwait(false);
            LogOpened(logger, recognized.File.Mod.Game.Source.Value, "a download outside the library");
            return new OpenedLink(going, LibraryPath: null);
        }

        LibraryDownload placed = await library.AddAsync(link, cancellationToken).ConfigureAwait(false);
        LogOpened(logger, recognized.File.Mod.Game.Source.Value, "the library");
        return new OpenedLink(placed.Queued, placed.Path);
    }

    private static bool IsDone(DownloadJobSnapshot job) => job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Cancelled;

    private static bool IsInLibrary(string root, DownloadJobSnapshot job)
        => LibraryPaths.RelativeTo(root, Path.Combine(job.TargetDirectory, job.FileName)) is not null;
}
