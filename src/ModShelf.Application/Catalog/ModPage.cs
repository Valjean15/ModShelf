using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Everything the site says about a mod, as <see cref="IModLookup"/> found it.
/// </summary>
/// <param name="Files">Every file the site lists, old and archived ones included, in no particular order.</param>
/// <param name="Changelogs">Each version's changes, in the order the site gives them.</param>
/// <param name="LinkedFile">The file the link points to: the one <c>download</c> with the same link would get.</param>
public sealed record ModPage(
    GameInfo Game,
    ModInfo Mod,
    IReadOnlyList<ModFile> Files,
    IReadOnlyList<ModChangelog> Changelogs,
    ModFileRef LinkedFile);
