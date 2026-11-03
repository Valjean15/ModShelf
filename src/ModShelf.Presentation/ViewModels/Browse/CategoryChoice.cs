namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A category for a search, as its box lists it: one of the game's, or every one.
/// </summary>
/// <param name="Category">The category's name on the site; null for every category.</param>
public sealed record CategoryChoice(string? Category, string Name)
{
    public override string ToString() => Name;
}
