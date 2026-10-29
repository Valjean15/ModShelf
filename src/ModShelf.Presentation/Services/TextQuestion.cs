namespace ModShelf.Presentation.Services;

/// <summary>
/// A question the user answers by typing or pasting text, such as the link a mod site hands over.
/// </summary>
/// <param name="Hint">Shown in the empty text box, such as an example of what to type.</param>
/// <param name="ConfirmLabel">The button that answers, saying what it does, such as "Download".</param>
public sealed record TextQuestion(string Title, string Message, string Hint, string ConfirmLabel)
{
    /// <summary>
    /// Whether the answer is a secret, such as an API key, which isn't shown as it's typed.
    /// </summary>
    public bool IsSecret { get; init; }

    /// <summary>
    /// A web page that helps the user answer, such as where they find their API key, which a button opens; null for none.
    /// </summary>
    public Uri? HelpPage { get; init; }

    /// <summary>
    /// The button that opens <see cref="HelpPage"/>, saying what it opens, such as "Open the API key page".
    /// </summary>
    public string? HelpPageLabel { get; init; }
}
