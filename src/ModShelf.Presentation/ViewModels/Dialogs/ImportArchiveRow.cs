namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// An archive as identified: its name, what it is, and whether importing copies it.
/// </summary>
/// <param name="Said">What it is, or why it's left alone.</param>
/// <param name="IsCopied">Whether importing copies it into the library.</param>
public sealed record ImportArchiveRow(string Name, string Said, bool IsCopied);
