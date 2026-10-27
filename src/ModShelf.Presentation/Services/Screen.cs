namespace ModShelf.Presentation.Services;

/// <summary>
/// The app's screens, in the order the sidebar lists them.
/// </summary>
public enum Screen
{
    /// <summary>The mods ModShelf has files of or watches, by game.</summary>
    Library,

    /// <summary>The updates the checks found, with their changelogs.</summary>
    Updates,

    /// <summary>A source's mods: search, look at a mod's files, download or watch.</summary>
    Browse,

    /// <summary>The download queue.</summary>
    Downloads,

    /// <summary>The accounts linked on each source.</summary>
    Accounts,

    /// <summary>The settings.</summary>
    Settings,
}
