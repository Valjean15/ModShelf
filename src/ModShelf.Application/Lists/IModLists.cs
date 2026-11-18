namespace ModShelf.Application.Lists;

/// <summary>
/// Exports the library as a list of mods and their files, and imports such a list (ADR 0098): the mods watched, and the
/// files the library lacks queued for it. Nothing is ever installed.
/// </summary>
public interface IModLists
{
    /// <summary>
    /// The library's mods the scope takes, each with its files but the old versions archived in <c>_archive</c>
    /// folders, by game, then by name. Nothing is asked of any site.
    /// </summary>
    Task<ModList> ExportAsync(ListScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// The list in its file format, <c>modshelf-list</c>, as JSON text.
    /// </summary>
    string Write(ModList list);

    /// <summary>
    /// Reads a list from its file format. A mod listed twice is read once, with the files of both.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The text isn't a ModShelf list, or one a newer ModShelf made, or one of its mods can't be read; the message says
    /// which.
    /// </exception>
    ModList Read(string text);

    /// <summary>
    /// Reads a collection on its site, such as one of Nexus Mods', as a list: its mods, in the order it lists them, each
    /// to watch, with the files it takes at the versions it pins, those it marks optional marked so; and what it needs
    /// from elsewhere, named. One request to the site. Nothing is downloaded yet, and nothing is ever installed.
    /// </summary>
    /// <returns>The list, titled with the collection's name; null when no site ModShelf reaches reads the link as one.</returns>
    /// <exception cref="ArgumentException">The link is to a site's collections, but can't be read as one.</exception>
    /// <exception cref="ModShelf.Sources.Abstractions.Accounts.AuthenticationFailedException">The site refused the account.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or has no such collection.</exception>
    Task<ModList?> ReadCollectionAsync(Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// What importing the list would do, from the library alone: which mods are new to it, which files it has, and which
    /// sites ModShelf can't reach here. Nothing is asked of any site.
    /// </summary>
    Task<ListImportPlan> PlanAsync(ModList list, CancellationToken cancellationToken);

    /// <summary>
    /// Imports a planned list, a mod at a time in its order. A new mod listed as watched is watched, with its pin and
    /// note; a mod the library has keeps its own. With <see cref="ListImportOptions.Download"/>, the listed files the
    /// library lacks are queued for it, as a link to each would queue it, after asking the mod's site for its files once;
    /// otherwise every listed mod is watched. One that can't be done doesn't stop the others: the result says why.
    /// </summary>
    /// <param name="progress">Told how many of the plan's mods are done after each.</param>
    /// <param name="queue">
    /// Queues a file's link for the library, answering whether it was queued, or false when the library has the file
    /// already; null for <see cref="Library.ILibraryDownloads.AddAsync"/> in this process. A host that hands its downloads
    /// to the process running the queue, as the MCP server does, gives its own.
    /// </param>
    Task<ListImportResult> ImportAsync(
        ListImportPlan plan,
        ListImportOptions options,
        IProgress<int>? progress,
        Func<Uri, CancellationToken, Task<bool>>? queue,
        CancellationToken cancellationToken);
}
