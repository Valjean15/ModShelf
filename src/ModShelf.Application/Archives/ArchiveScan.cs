using ModShelf.Domain.Mods;

namespace ModShelf.Application.Archives;

/// <summary>
/// The archives in a folder, for a game, as found on disk: nothing asked of any site yet (ADR 0101).
/// </summary>
/// <param name="Folder">The folder, as a full path.</param>
/// <param name="Archives">Its archives, by name.</param>
/// <param name="OtherFiles">How many of its files aren't archives, such as readmes or mod managers' notes.</param>
/// <param name="Requests">
/// About how many requests identifying the archives takes from the game's site: one for each mod a note or a name gives,
/// and one more for one new to the library; one for each other archive, when the site finds files by hash. An archive
/// the library has already takes none, and one a note or a name misleads takes one more.
/// </param>
/// <param name="CanLookUp">Whether the site finds files by hash, so archives without a note or a name can be identified.</param>
public sealed record ArchiveScan(
    string Folder,
    GameRef Game,
    IReadOnlyList<ScannedArchive> Archives,
    int OtherFiles,
    int Requests,
    bool CanLookUp);
