namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A time window for a search, as its box lists it: how recently the mods were updated, such as "Updated in the last
/// week".
/// </summary>
/// <param name="Within">How long ago at most; null for any time.</param>
public sealed record TimeWindowChoice(TimeSpan? Within, string Name)
{
    public override string ToString() => Name;
}
