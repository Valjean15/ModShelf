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
}
