namespace ModShelf.Desktop.Views.Dialogs;

/// <summary>
/// What a <see cref="ChoiceDialog"/> asks, and its choices, the first the main one.
/// </summary>
internal sealed class ChoiceDialogContent(string title, string message, IReadOnlyList<string> choices)
{
    public string Title { get; } = title;

    public string Message { get; } = message;

    public IReadOnlyList<ChoiceDialogButton> Choices { get; } = [.. choices.Select((label, index) => new ChoiceDialogButton(label, index))];
}
