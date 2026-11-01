namespace ModShelf.Cli.Mcp;

/// <summary>
/// What became of a download, or of a change to one.
/// </summary>
/// <param name="Download">The download as the request left it; null when nothing downloads, as for a file the library has.</param>
/// <param name="LibraryPath">For a download into the library, where the file is or goes, an absolute path.</param>
/// <param name="RunBy">What runs the queue: <c>desktop app</c>, or <c>this MCP server</c> while no desktop app runs.</param>
/// <param name="Message">What happened, and what to expect, in a sentence or two.</param>
internal sealed record DownloadResult(
    DownloadEntry? Download,
    string? LibraryPath,
    bool AlreadySaved,
    bool AlreadyQueued,
    string RunBy,
    string Message);
