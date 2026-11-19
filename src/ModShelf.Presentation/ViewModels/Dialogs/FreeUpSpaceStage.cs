namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Where freeing up space stands in its dialog.
/// </summary>
public enum FreeUpSpaceStage
{
    /// <summary>
    /// Listing what could go, asking the mods' sites which files they still offer.
    /// </summary>
    Listing,

    /// <summary>
    /// Showing what could go, by group, for the user to choose from.
    /// </summary>
    Listed,

    /// <summary>
    /// Deleting the chosen files, one at a time.
    /// </summary>
    Deleting,

    /// <summary>
    /// Done, stopped or failed, saying what it did.
    /// </summary>
    Done,
}
