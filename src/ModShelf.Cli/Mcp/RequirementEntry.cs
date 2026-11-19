namespace ModShelf.Cli.Mcp;

/// <summary>
/// Something a linked mod needs, as its site lists it, and what <c>download</c> did about it.
/// </summary>
/// <param name="Name">What's needed, as the mod's author named it.</param>
/// <param name="Page">Its page: the mod's on the site, or the thing's own site; null when the site gives none.</param>
/// <param name="NeededBy">The requirement that needs it, by name; null when the linked mod itself does.</param>
/// <param name="Depth">1 for what the linked mod needs, 2 for what those need, and 3 below that.</param>
/// <param name="State">
/// <c>download</c> when it downloads with the call (its download says how); <c>inLibrary</c> or <c>downloading</c> when
/// the library has it or the queue does already; <c>elsewhere</c> for a thing off the site, which the user gets from its
/// page; <c>cantDownload</c> when the site has no file of it to download.
/// </param>
/// <param name="Notes">What the author said of it, such as which files need it; null when nothing.</param>
/// <param name="File">The file that downloads: the mod's newest main file's name; null when none does.</param>
/// <param name="Download">For one that downloads, what became of its file's link, as each link's result says.</param>
/// <param name="Message">What became of it, in a sentence.</param>
internal sealed record RequirementEntry(
    string Name,
    string? Page,
    string? NeededBy,
    int Depth,
    string State,
    string? Notes,
    string? File,
    LinkDownloadResult? Download,
    string Message);
