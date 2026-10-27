namespace ModShelf.Presentation.Services;

/// <summary>
/// A question the user answers by typing or pasting text, such as the link a mod site hands over.
/// </summary>
/// <param name="Hint">Shown in the empty text box, such as an example of what to type.</param>
/// <param name="ConfirmLabel">The button that answers, saying what it does, such as "Download".</param>
public sealed record TextQuestion(string Title, string Message, string Hint, string ConfirmLabel);
