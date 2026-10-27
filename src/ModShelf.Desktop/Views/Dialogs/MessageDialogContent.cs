namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// What a <see cref="MessageDialog"/> says, and its buttons.
/// </summary>
/// <param name="CancelLabel">The button that turns it down; null for a message with one button.</param>
internal sealed record MessageDialogContent(string Title, string Message, string ConfirmLabel, string? CancelLabel)
{
    public bool HasCancel => CancelLabel is not null;
}
