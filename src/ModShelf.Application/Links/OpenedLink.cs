using ModShelf.Application.Downloads;

namespace ModShelf.Application.Links;

/// <summary>
/// What <see cref="IOpenedLinks.OpenAsync"/> did with a link.
/// </summary>
/// <param name="Download">
/// The download that goes on with the link, or was added for it; null when the file is in the library and on disk
/// already.
/// </param>
/// <param name="LibraryPath">
/// Where the file is, or goes, in the library: an absolute path under the library root; null when a download outside
/// the library went on with the link.
/// </param>
public sealed record OpenedLink(LinkDownload? Download, string? LibraryPath);
