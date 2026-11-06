namespace ModShelf.Presentation.ViewModels.QuickSearch;

/// <summary>
/// What a row of quick search is.
/// </summary>
public enum QuickSearchKind
{
    /// <summary>
    /// A search made before, to make again.
    /// </summary>
    Recent,

    /// <summary>
    /// A game, opening in the Library, or in Browse when the library has none of its mods.
    /// </summary>
    Game,

    /// <summary>
    /// A mod in the library, opening its page.
    /// </summary>
    Mod,

    /// <summary>
    /// A part of the settings, or a game's own settings.
    /// </summary>
    Setting,

    /// <summary>
    /// A screen.
    /// </summary>
    Screen,
}
