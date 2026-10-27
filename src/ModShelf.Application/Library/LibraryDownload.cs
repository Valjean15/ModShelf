using ModShelf.Application.Downloads;

namespace ModShelf.Application.Library;

/// <summary>
/// What <see cref="ILibraryDownloads.AddAsync"/> did with a link.
/// </summary>
/// <param name="Path">Where the file is, or goes: an absolute path under the library root.</param>
/// <param name="Queued">The job that downloads it; or null when the file is in the library and on disk already.</param>
public sealed record LibraryDownload(string Path, LinkDownload? Queued);
