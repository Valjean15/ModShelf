using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Downloads;

/// <summary>
/// Turns a mod file into something the download engine can fetch.
/// </summary>
/// <remarks>
/// The engine calls this every time a download starts or resumes, and again when a ticket expires
/// mid-download. Tickets are short-lived, so never store or cache them.
/// </remarks>
public interface IDownloadResolver : IModSource
{
    /// <param name="file">One of this source's files.</param>
    /// <param name="handoff">
    /// The link that completes a handoff for <paramref name="file"/>: the one the source's website handed over
    /// after the user started the download there (see <see cref="HandoffRequired"/>), such as an <c>nxm://</c>
    /// link. Null when there is none. It may have expired or been used up, and it can carry a key, so never store
    /// or log it.
    /// </param>
    /// <returns>
    /// A <see cref="DownloadTicket"/> when the file can be downloaded right away, or
    /// <see cref="HandoffRequired"/> when the user has to start the download on the source's website, including
    /// when the site refuses <paramref name="handoff"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="file"/> isn't this source's.</exception>
    Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken);
}
