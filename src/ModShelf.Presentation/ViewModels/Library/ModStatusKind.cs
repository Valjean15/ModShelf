namespace ModShelf.Presentation.ViewModels.Library;

/// <summary>
/// What a mod's status says, for how it looks.
/// </summary>
public enum ModStatusKind
{
    /// <summary>Something to download, such as an update.</summary>
    Update,

    /// <summary>Something wrong, such as a file missing from disk.</summary>
    Problem,

    /// <summary>Something to know, such as a mod watched without files.</summary>
    Note,
}
