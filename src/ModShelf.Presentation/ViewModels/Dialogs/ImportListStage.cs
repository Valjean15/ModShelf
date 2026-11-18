namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Where an import of a list stands in its dialog.
/// </summary>
public enum ImportListStage
{
    /// <summary>
    /// Saying what importing would do, with the choices.
    /// </summary>
    Asking,

    /// <summary>
    /// Importing, a mod at a time.
    /// </summary>
    Importing,

    /// <summary>
    /// Done, stopped or failed, saying what it did.
    /// </summary>
    Done,
}
