namespace ModShelf.Cli.Mcp;

/// <summary>
/// The download queue, the oldest first.
/// </summary>
internal sealed record DownloadListing(IReadOnlyList<DownloadEntry> Downloads);
