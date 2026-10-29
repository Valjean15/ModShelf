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
    /// What the text box shows for each character of a secret, such as an API key; '\0' to show the text as it is.
    /// </summary>
    public char PasswordChar { get; init; }

    /// <summary>
    /// A web page that helps the user answer, which a button opens; null for none.
    /// </summary>
    public Uri? HelpPage { get; init; }

    public string? HelpPageLabel { get; init; }

    public bool HasHelpPage => HelpPage is not null && !string.IsNullOrWhiteSpace(HelpPageLabel);

    /// <summary>
    /// Opens a page in the browser, saying whether it opened.
    /// </summary>
    public Func<Uri, Task<bool>>? OpenPage { get; init; }

    /// <summary>
    /// What the user typed or pasted.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
