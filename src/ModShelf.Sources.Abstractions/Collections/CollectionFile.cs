using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Collections;

/// <summary>
/// A file a collection takes from one of the site's mods, at the version the collection pins.
/// </summary>
public sealed record CollectionFile
{
    /// <param name="optional">Whether the collection marks it optional, for the user to choose.</param>
    /// <exception cref="ArgumentException">The mod's name or the file's is blank.</exception>
    public CollectionFile(
        ModFileRef file,
        string modName,
        string fileName,
        string? version = null,
        ModFileCategory category = ModFileCategory.Unknown,
        bool optional = false,
        string? author = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(modName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        File = file;
        ModName = modName.Trim();
        FileName = fileName.Trim();
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        Category = category;
        Optional = optional;
        Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim();
    }

    public ModFileRef File { get; }

    /// <summary>
    /// The name of the file's mod.
    /// </summary>
    public string ModName { get; }

    /// <summary>
    /// The file's name on its mod's Files tab.
    /// </summary>
    public string FileName { get; }

    public string? Version { get; }

    public ModFileCategory Category { get; }

    /// <summary>
    /// Whether the collection marks the file optional, for the user to choose.
    /// </summary>
    public bool Optional { get; }

    /// <summary>
    /// Who the site says made the mod; null when it doesn't say.
    /// </summary>
    public string? Author { get; }
}
