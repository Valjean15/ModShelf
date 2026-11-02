namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// What a game's mods can be narrowed to on the Library screen.
/// </summary>
public enum ModFilterKind
{
    /// <summary>Mods with updates found.</summary>
    HasUpdates,

    /// <summary>Mods watched for updates.</summary>
    Watched,

    /// <summary>Mods with old versions in an <c>_archive</c> folder.</summary>
    Archived,

    /// <summary>Mods with files the library has that aren't on disk.</summary>
    Missing,
}
