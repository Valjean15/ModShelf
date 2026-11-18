using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Archives;

/// <summary>
/// An archive as identified, and what importing does with it (ADR 0101).
/// </summary>
/// <param name="Path">Its full path.</param>
/// <param name="State">What importing does with it.</param>
/// <param name="By">How it was identified, when it was.</param>
/// <param name="File">The site's file it is, when known.</param>
/// <param name="ModName">Its mod's name, when known.</param>
/// <param name="FileName">The site's name for the file, such as "SkyUI 5.2", when known.</param>
/// <param name="Why">Why it's unknown.</param>
/// <param name="Sha256">The SHA-256 of its content, when it could be read.</param>
public sealed record PlannedArchive(
    string Path,
    long SizeInBytes,
    ArchiveState State,
    ArchiveClue? By = null,
    ModFileRef? File = null,
    string? ModName = null,
    string? FileName = null,
    string? Why = null,
    string? Sha256 = null)
{
    /// <summary>
    /// Its file name.
    /// </summary>
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// The mod as its site describes it, for an identified archive whose mod is new to the library.
    /// </summary>
    internal ModInfo? Mod { get; init; }

    /// <summary>
    /// The file as its site lists it, for an identified archive.
    /// </summary>
    internal ModFile? SiteFile { get; init; }
}
