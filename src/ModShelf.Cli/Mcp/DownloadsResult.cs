namespace ModShelf.Cli.Mcp;

/// <summary>
/// What became of the links given to download: one result each, in the order given.
/// </summary>
/// <param name="Message">How many are downloading, were in the library already, or couldn't be downloaded.</param>
internal sealed record DownloadsResult(IReadOnlyList<LinkDownloadResult> Results, string Message);
