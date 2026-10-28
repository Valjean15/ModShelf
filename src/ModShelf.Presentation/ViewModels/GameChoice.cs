using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A game to browse, as the game box lists it.
/// </summary>
/// <param name="Name">Its name, with its site's when more than one site can search.</param>
public sealed record GameChoice(GameRef Game, string Name)
{
    /// <summary>
    /// The name, which the game box shows and filters by.
    /// </summary>
    public override string ToString() => Name;
}
