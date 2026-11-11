using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// One of the sites Browse's site box offers, or all of them.
/// </summary>
public sealed record SiteChoice
{
    internal SiteChoice(SourceId? source, string name)
    {
        Source = source;
        Name = name;
    }

    /// <summary>
    /// The site; null for all of them.
    /// </summary>
    public SourceId? Source { get; }

    /// <summary>
    /// Such as "Nexus Mods", or "All sites".
    /// </summary>
    public string Name { get; }

    public override string ToString() => Name;
}
