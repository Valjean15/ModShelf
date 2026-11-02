namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// How the Library's games are ordered.
/// </summary>
public enum GameOrder
{
    /// <summary>The games with the most mods first.</summary>
    MostMods,

    Name,

    /// <summary>The games whose files take the most space first, archived versions included.</summary>
    MostSpace,

    /// <summary>The games with the most updates found first.</summary>
    UpdatesFirst,
}
