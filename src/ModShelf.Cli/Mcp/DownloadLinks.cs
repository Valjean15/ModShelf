namespace ModShelf.Cli.Mcp;

/// <summary>
/// The arguments <c>download</c> takes: the links to download, in order.
/// </summary>
internal sealed record DownloadLinks(IReadOnlyList<string> Links);
