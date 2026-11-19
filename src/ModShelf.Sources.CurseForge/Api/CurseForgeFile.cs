namespace ModShelf.Sources.CurseForge.Api;

/// <summary>
/// A file of a mod on CurseForge: one build, for the game versions and loaders it lists.
/// </summary>
/// <param name="ReleaseType">1 a release, 2 a beta, 3 an alpha.</param>
/// <param name="FileStatus">Where it is in CurseForge's review: 4 approved and 10 released are offered; 8 archived and 12 deprecated withdrawn.</param>
/// <param name="DownloadUrl">Its download link; null when its mod's author doesn't let apps download it.</param>
internal sealed record CurseForgeFile(
    long Id,
    long GameId,
    long ModId,
    bool IsAvailable,
    string? DisplayName,
    string? FileName,
    int ReleaseType,
    int FileStatus,
    IReadOnlyList<CurseForgeHash>? Hashes,
    DateTimeOffset FileDate,
    long FileLength,
    string? DownloadUrl,
    IReadOnlyList<string>? GameVersions,
    IReadOnlyList<CurseForgeDependency>? Dependencies);
