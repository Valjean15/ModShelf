namespace ModShelf.Cli.Mcp;

/// <summary>
/// A download in the queue.
/// </summary>
/// <param name="Path">Where the file goes, an absolute path.</param>
/// <param name="HandoffPage">
/// For a download that waits for the user, the page where they start it on the site, as free Nexus Mods accounts do.
/// </param>
internal sealed record DownloadEntry(
    string Id,
    string Status,
    string Source,
    string Game,
    string ModId,
    string FileId,
    string FileName,
    string Folder,
    string Path,
    long BytesDownloaded,
    long? TotalBytes,
    string? Failure,
    DateTimeOffset CreatedAt,
    string? HandoffPage);
