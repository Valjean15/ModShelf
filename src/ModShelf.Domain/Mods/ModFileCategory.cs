namespace ModShelf.Domain.Mods;

/// <summary>
/// How a source classifies a file. Each source maps its own categories onto these.
/// </summary>
/// <remarks>The numeric values are stored, so never renumber them.</remarks>
public enum ModFileCategory
{
    /// <summary>The source doesn't classify the file, or uses a category with no equivalent here.</summary>
    Unknown = 0,

    /// <summary>The mod's main download.</summary>
    Main = 1,

    /// <summary>A patch or update applied on top of a main file.</summary>
    Update = 2,

    /// <summary>An optional add-on or variant.</summary>
    Optional = 3,

    /// <summary>Anything else the author publishes, such as documentation or tools.</summary>
    Miscellaneous = 4,

    /// <summary>A file the author replaced with a newer version.</summary>
    OldVersion = 5,

    /// <summary>A file the source no longer offers for download.</summary>
    Archived = 6,
}
