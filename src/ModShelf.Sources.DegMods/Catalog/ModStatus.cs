namespace ModShelf.Sources.DegMods.Catalog;

/// <summary>
/// What a mod asked for by itself turned out to be.
/// </summary>
internal enum ModStatus
{
    /// <summary>
    /// DEG Mods shows it.
    /// </summary>
    Shown,

    /// <summary>
    /// No relay holds it, or its author deleted it.
    /// </summary>
    Missing,

    /// <summary>
    /// DEG Mods' moderation leaves it out.
    /// </summary>
    Hidden,
}
