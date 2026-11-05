namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A time window for a search, as its box lists it: how recently the mods were updated, such as "Updated in the last
/// week".
/// </summary>
/// <param name="Within">How long ago at most; null for any time.</param>
public sealed record TimeWindowChoice(TimeSpan? Within, string Name)
{
    /// <summary>
    /// The name in a word or two, where it's said what it's for, such as "Week" under "Updated within".
    /// </summary>
    public string ShortName { get; init; } = Name;

    public override string ToString() => Name;
}
