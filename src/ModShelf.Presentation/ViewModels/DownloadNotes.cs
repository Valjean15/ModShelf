using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// What the screens say became of a link added for a download.
/// </summary>
internal static class DownloadNotes
{
    /// <summary>
    /// Adds the link's file to the library, from a screen other than Downloads, and tells what became of it, or why it
    /// couldn't be added. Another failure is thrown, for the command to log.
    /// </summary>
    /// <param name="tell">Shows a note, and whether it's a problem.</param>
    public static async Task AddToLibraryAsync(ILibraryDownloads downloads, Uri link, Action<string, bool> tell, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(tell);

        LibraryDownload placed;
        try
        {
            placed = await downloads.AddAsync(link, cancellationToken);
        }
        catch (Exception exception) when (LinkProblems.Describe(exception, library: true) is { } problem)
        {
            tell(problem, true);
            return;
        }

        tell(
            placed.Queued switch
            {
                null => InLibraryAlready(placed),
                { AlreadyQueued: true } => Strings.UpdateDownloadingAlready,
                _ => Text.Format(Strings.UpdateDownloading, Path.GetDirectoryName(placed.Path)),
            },
            false);
    }

    /// <summary>
    /// Such as "C:\Mods\SkyUI\SkyUI_5_2_SE.7z is in the library already.": the library had the file, so nothing was
    /// queued.
    /// </summary>
    public static string InLibraryAlready(LibraryDownload placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        return Text.Format(Strings.DownloadsInLibraryAlready, placed.Path);
    }

    /// <summary>
    /// What became of a link added for a download: saved already, downloading, or going on with a download added before.
    /// </summary>
    /// <param name="onDownloadsScreen">Whether it's said on the Downloads screen, which lists the download; another
    /// screen says it's there.</param>
    public static string Added(LinkDownload added, bool onDownloadsScreen)
    {
        ArgumentNullException.ThrowIfNull(added);

        DownloadJobSnapshot job = added.Job;
        return added switch
        {
            { AlreadySaved: true } => Text.Format(Strings.DownloadsSavedAlready, job.FileName, job.TargetDirectory),
            { AlreadyQueued: true } => onDownloadsScreen ? Text.Format(Strings.DownloadsContinuing, job.FileName) : Strings.UpdateDownloadingAlready,
            _ => Text.Format(Strings.DownloadsDownloadingInto, job.FileName, job.TargetDirectory),
        };
    }
}
