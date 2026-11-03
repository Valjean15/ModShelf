namespace ModShelf.Cli.Mcp;

/// <summary>
/// The older version a file downloaded into the library replaced, as the library's history says, and what became of it.
/// </summary>
/// <param name="FileId">Its ID on its site.</param>
/// <param name="Version">Its version, when its site said one.</param>
/// <param name="Name">Its name on its site, when the library or its history still says it.</param>
/// <param name="Became">
/// What became of it: archived, moved to an <c>_archive</c> folder; deleted, from disk and the library; kept, where it
/// was; or gone, out of the library otherwise, such as by a rescan that didn't find it.
/// </param>
internal sealed record ReplacedFileEntry(string FileId, string? Version, string? Name, string Became);
