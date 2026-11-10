using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Search;

/// <summary>
/// One page of the mods a search found.
/// </summary>
public sealed record SearchPage
{
    /// <param name="totalCount">How many mods match in all, as the site counts them.</param>
    /// <param name="offset">How many results come before this page.</param>
    /// <param name="leftOut">
    /// How many of the page's results were left out of <paramref name="mods"/>: the adult mods a site marks but can't
    /// leave out itself (<see cref="SearchCapabilities.MarksAdultContent"/>).
    /// </param>
    public SearchPage(IEnumerable<ModInfo> mods, int totalCount, int offset, int leftOut = 0)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(leftOut);

        Mods = [.. mods];
        if (Mods.Any(mod => mod is null))
        {
            throw new ArgumentException("A page can't hold a null mod.", nameof(mods));
        }

        TotalCount = totalCount;
        Offset = offset;
        LeftOut = leftOut;
    }

    /// <summary>
    /// The mods on this page, in the order asked for.
    /// </summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    public int TotalCount { get; }

    public int Offset { get; }

    /// <summary>
    /// How many of the page's results were left out of <see cref="Mods"/>, as adult mods are when the site can't leave
    /// them out itself; they still count in <see cref="TotalCount"/>.
    /// </summary>
    public int LeftOut { get; }

    /// <summary>
    /// The offset of the page after this one: past the mods on it and those left out.
    /// </summary>
    public int NextOffset => Offset + Mods.Count + LeftOut;

    /// <summary>
    /// Whether more mods match after this page.
    /// </summary>
    public bool HasMore => NextOffset < TotalCount;
}
