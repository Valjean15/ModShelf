using ModShelf.Presentation.ViewModels.FirstRun;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Services;

/// <summary>
/// Dialogs over the main window. Desktop shows them with Avalonia; ViewModel tests answer them.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a message until the user closes it.
    /// </summary>
    Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the user to confirm something.
    /// </summary>
    /// <param name="confirmLabel">The button that confirms, saying what it does, such as "Delete".</param>
    /// <returns>Whether the user confirmed; false when they cancelled or closed the dialog.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the user to choose one of a few ways on, each a button, the first the main one.
    /// </summary>
    /// <param name="choices">Each button's label, saying what it does, such as "Quit ModShelf".</param>
    /// <returns>The index of the choice; null when they closed the dialog without choosing.</returns>
    Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the user to type or paste text.
    /// </summary>
    /// <returns>What the user entered, as typed; null when they cancelled or closed the dialog.</returns>
    Task<string?> AskTextAsync(TextQuestion question, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the user to pick a folder.
    /// </summary>
    /// <returns>The folder's full path; null when they cancelled, or picked a place that isn't a folder on this computer.</returns>
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the user to find a game to add, by its name or its key, as Settings' Add a game… does, until they choose one,
    /// type a key and add it (<see cref="AddGameViewModel.AddRequested"/>), or close the dialog.
    /// </summary>
    /// <returns>Whether they added one: <see cref="AddGameViewModel.Game"/> or <see cref="AddGameViewModel.TypedKey"/> says
    /// which.</returns>
    Task<bool> AddGameAsync(AddGameViewModel adding, CancellationToken cancellationToken);

    /// <summary>
    /// Shows the dialog that moves a game's files, until it asks to close (<see cref="GameMoveViewModel.CloseRequested"/>):
    /// once they've moved, when there was nothing to move, or when the user closes it, which can't happen while they move.
    /// </summary>
    Task MoveGameAsync(GameMoveViewModel moving, CancellationToken cancellationToken);

    /// <summary>
    /// Shows the first-run setup over the main window, until it asks to close (<see cref="FirstRunViewModel.CloseRequested"/>)
    /// or the user closes it.
    /// </summary>
    Task ShowFirstRunAsync(FirstRunViewModel setup, CancellationToken cancellationToken);
}
