namespace ModShelf.Cli.Mcp;

/// <summary>
/// What importing a list would do, or did.
/// </summary>
/// <param name="Preview">Whether nothing was done yet: the counts say what importing would do.</param>
/// <param name="Title">The list's title, if it has one.</param>
/// <param name="Mods">How many mods it lists.</param>
/// <param name="NewMods">How many the library doesn't have.</param>
/// <param name="ModsInLibrary">How many the library has.</param>
/// <param name="ModsWithoutSite">How many are of sites ModShelf can't reach here.</param>
/// <param name="FilesToDownload">How many files importing downloads, as asked.</param>
/// <param name="OptionalFilesLeftOut">How many files the list marks optional that aren't downloaded, unless asked.</param>
/// <param name="ModsAdded">How many mods were watched, new to the library; 0 in a preview.</param>
/// <param name="FilesQueued">How many files were queued for the library; 0 in a preview.</param>
/// <param name="FilesInLibrary">How many listed files the library has already.</param>
/// <param name="RunBy">What runs the queue: <c>desktop app</c>, <c>this MCP server</c>, or null when nothing was queued.</param>
/// <param name="Problems">What couldn't be done, in the list's order.</param>
/// <param name="Elsewhere">What a collection needs from elsewhere, which isn't downloaded; empty for a list file.</param>
/// <param name="FilesUnavailable">How many files a collection names that its site no longer offers.</param>
internal sealed record ListImportEntry(
    bool Preview,
    string? Title,
    int Mods,
    int NewMods,
    int ModsInLibrary,
    int ModsWithoutSite,
    int FilesToDownload,
    int OptionalFilesLeftOut,
    int ModsAdded,
    int FilesQueued,
    int FilesInLibrary,
    string? RunBy,
    IReadOnlyList<ListProblemEntry> Problems,
    IReadOnlyList<ListElsewhereEntry> Elsewhere,
    int FilesUnavailable,
    string Message);
