namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// One of a mod's files, from <c>v1/games/{game}/mods/{mod}/files/{file}.json</c> or the list of a mod's files.
/// </summary>
/// <param name="CategoryId">Which section of the Files tab it's in: <see cref="MainCategory"/> for the main files.</param>
/// <param name="UploadedTimestamp">When it was uploaded, in seconds since 1970.</param>
internal sealed record NexusFile(long FileId, string? Name, string? FileName, int CategoryId, long UploadedTimestamp)
{
    /// <summary>
    /// The category of a mod's main files. The others are updates, optional files, old versions, miscellaneous
    /// files and archived ones.
    /// </summary>
    public const int MainCategory = 1;
}
