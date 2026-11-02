namespace ModShelf.Cli.Mcp;

/// <summary>
/// What became of one of the links given to download.
/// </summary>
/// <param name="Download">The download as the request left it; null when nothing downloads, as for a file the library has
/// or a link that couldn't be downloaded.</param>
/// <param name="LibraryPath">Where the file is or goes, an absolute path; null when the link couldn't be downloaded.</param>
/// <param name="RunBy">What runs the queue: <c>desktop app</c>, or <c>this MCP server</c> while no desktop app runs; null
/// when the link couldn't be downloaded.</param>
/// <param name="Problem">Why the link couldn't be downloaded; null when it was.</param>
/// <param name="Message">What happened, and what to expect, in a sentence or two.</param>
internal sealed record LinkDownloadResult(
    DownloadEntry? Download,
    string? LibraryPath,
    bool AlreadySaved,
    bool AlreadyQueued,
    string? RunBy,
    string? Problem,
    string Message);
