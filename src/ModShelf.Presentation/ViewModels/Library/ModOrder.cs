namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// How a game's mods are ordered on the Library screen. Mods that tie are ordered by name.
/// </summary>
public enum ModOrder
{
    Name,

    /// <summary>The mods with updates found first.</summary>
    UpdatesFirst,

    /// <summary>The mods their sites updated last first, as their saved details say; the ones without a date last.</summary>
    UpdatedOnSite,

    /// <summary>The mods whose files take the most space first.</summary>
    Size,

    /// <summary>The mods that came into the library last first.</summary>
    AddedToLibrary,
}
