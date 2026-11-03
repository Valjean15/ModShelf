using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library;

/// <summary>
/// Something that happened in the library, as the history keeps it.
/// </summary>
/// <param name="At">When it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Mod">The mod it happened to.</param>
/// <param name="ModName">The mod's name then.</param>
/// <param name="File">The file it happened to; null when it happened to the mod, as when it was removed.</param>
/// <param name="FileName">The file's name on its site, such as "SkyUI 5.2"; null without a file.</param>
/// <param name="VersionLabel">The file's version, such as 5.2, when its site says one.</param>
/// <param name="RelativePath">Where the file is, or was, under the library's folder.</param>
/// <param name="Replaces">For a download, the older version it replaced, of the same mod; null for none.</param>
/// <param name="ReplacesVersion">The version of the file it replaced, when its site said one.</param>
/// <param name="ByRule">For a deletion, whether the rule for old versions deleted the file, rather than the user.</param>
public sealed record LibraryEvent(
    DateTimeOffset At,
    LibraryEventKind Kind,
    ModRef Mod,
    string ModName,
    ModFileRef? File = null,
    string? FileName = null,
    string? VersionLabel = null,
    string? RelativePath = null,
    ModFileRef? Replaces = null,
    string? ReplacesVersion = null,
    bool ByRule = false)
{
    /// <summary>
    /// What happened to <paramref name="file"/> of <paramref name="mod"/>.
    /// </summary>
    internal static LibraryEvent Of(DateTimeOffset at, LibraryEventKind kind, LibraryMod mod, LibraryFile file, bool byRule = false)
        => new(at, kind, mod.Mod, mod.Name, file.File, file.Name, file.VersionLabel, file.RelativePath, ByRule: byRule);
}
