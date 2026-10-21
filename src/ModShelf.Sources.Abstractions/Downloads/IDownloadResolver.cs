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
    /// <returns>
    /// A <see cref="DownloadTicket"/> when the file can be downloaded right away, or
    /// <see cref="HandoffRequired"/> when the user has to start the download on the source's website.
    /// </returns>
    Task<ResolveResult> ResolveAsync(ModFileRef file, CancellationToken cancellationToken);
}
