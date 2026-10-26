using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// One page of the mods a search found.
/// </summary>
public sealed record SearchPage
{
    /// <param name="totalCount">How many mods match in all, as the site counts them.</param>
    /// <param name="offset">How many results come before this page.</param>
    public SearchPage(IEnumerable<ModInfo> mods, int totalCount, int offset)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        Mods = [.. mods];
        if (Mods.Any(mod => mod is null))
        {
            throw new ArgumentException("A page can't hold a null mod.", nameof(mods));
        }

        TotalCount = totalCount;
        Offset = offset;
    }

    /// <summary>
    /// The mods on this page, in the order asked for.
    /// </summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    public int TotalCount { get; }

    public int Offset { get; }

    /// <summary>
    /// Whether more mods match after this page.
    /// </summary>
    public bool HasMore => Offset + Mods.Count < TotalCount;
}
