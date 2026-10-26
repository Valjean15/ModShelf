using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Links;

/// <summary>
/// A link that a source recognized as one of its files.
/// </summary>
public sealed record RecognizedLink
{
    /// <exception cref="ArgumentException">The link is both a catch-all match and a handoff.</exception>
    public RecognizedLink(ModFileRef file, string? suggestedFileName = null, bool isCatchAll = false, bool isHandoff = false)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (isCatchAll && isHandoff)
        {
            throw new ArgumentException("Only a source's own links complete a handoff, so a catch-all match can't.", nameof(isHandoff));
        }

        File = file;
        SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName) ? null : suggestedFileName;
        IsCatchAll = isCatchAll;
        IsHandoff = isHandoff;
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

    /// <summary>
    /// Whether the link completes a handoff: the source's website handed it over after the user started the
    /// download there, as Nexus Mods does with <c>nxm://</c> links. The engine passes it to
    /// <see cref="Downloads.IDownloadResolver.ResolveAsync"/> for the file, keeps it in memory only, and never
    /// logs it, because it can carry a key.
    /// </summary>
    public bool IsHandoff { get; }
}
