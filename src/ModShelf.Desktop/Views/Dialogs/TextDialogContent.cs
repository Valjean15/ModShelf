namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// What a <see cref="TextDialog"/> asks, and the text the user enters.
/// </summary>
internal sealed class TextDialogContent(string title, string message, string hint, string confirmLabel, string cancelLabel)
{
    public string Title { get; } = title;

    public string Message { get; } = message;

    public string Hint { get; } = hint;

    public string ConfirmLabel { get; } = confirmLabel;

    public string CancelLabel { get; } = cancelLabel;

    /// <summary>
    /// What the user typed or pasted.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
