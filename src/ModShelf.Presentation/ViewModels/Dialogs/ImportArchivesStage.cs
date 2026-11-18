namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Where an import of archives stands in its dialog.
/// </summary>
public enum ImportArchivesStage
{
    /// <summary>
    /// Saying how many archives there are, and the requests identifying them takes.
    /// </summary>
    Scanned,

    /// <summary>
    /// Identifying them, an archive at a time.
    /// </summary>
    Identifying,

    /// <summary>
    /// Saying what each is, and what importing would copy.
    /// </summary>
    Identified,

    /// <summary>
    /// Copying them into the library, an archive at a time.
    /// </summary>
    Importing,

    /// <summary>
    /// Done, stopped or failed, saying what it did.
    /// </summary>
    Done,
}
