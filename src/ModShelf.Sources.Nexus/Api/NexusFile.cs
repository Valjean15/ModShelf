namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One of a mod's files, from <c>v1/games/{game}/mods/{mod}/files/{file}.json</c> or the list of a mod's files.
/// </summary>
/// <param name="CategoryId">Which section of the Files tab it's in: <see cref="MainCategory"/> for the main files.</param>
/// <param name="CategoryName">The section's name, such as <c>MAIN</c> or <c>OLD_VERSION</c>; missing for some.</param>
/// <param name="UploadedTimestamp">When it was uploaded, in seconds since 1970.</param>
/// <param name="SizeKb">The size in kilobytes, for files without <paramref name="SizeInBytes"/>.</param>
internal sealed record NexusFile(
    long FileId,
    string? Name,
    string? FileName,
    int CategoryId,
    long UploadedTimestamp,
    string? CategoryName = null,
    string? Version = null,
    long? SizeInBytes = null,
    long? SizeKb = null)
{
    /// <summary>
    /// The category of a mod's main files. The others are updates (2), optional files (3), old versions (4) and
    /// miscellaneous files (5).
    /// </summary>
    public const int MainCategory = 1;
}
