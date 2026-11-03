namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// How many mods a page lists, as its box lists it, such as "50 per page".
/// </summary>
public sealed record PageSizeChoice(int Count, string Name)
{
    public override string ToString() => Name;
}
