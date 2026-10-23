using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Links;

/// <summary>
/// A link that a source recognized as one of its files.
/// </summary>
public sealed record RecognizedLink
{
    public RecognizedLink(ModFileRef file, string? suggestedFileName = null, bool isCatchAll = false)
    {
        ArgumentNullException.ThrowIfNull(file);

        File = file;
        SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? null : suggestedFileName;
        IsCatchAll = isCatchAll;
    }

    public ModFileRef File { get; }

    /// <summary>
    /// The name to save the file under when the user doesn't choose one, or null when the link doesn't tell.
    /// </summary>
    public string? SuggestedFileName { get; }

    /// <summary>
    /// Whether the source claims any link it can fetch, as plain direct links do, rather than only its own
    /// site's links. A site-specific match wins over a catch-all one.
    /// </summary>
    public bool IsCatchAll { get; }
}
