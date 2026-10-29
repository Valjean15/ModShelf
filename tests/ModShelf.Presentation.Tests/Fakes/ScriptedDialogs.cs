using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Dialogs the test answers ahead, recording what each showed. With no answer left, the user cancels.
/// </summary>
internal sealed class ScriptedDialogs : IDialogService
{
    public List<(string Title, string Message)> Messages { get; } = [];

    public List<(string Title, string Message, string ConfirmLabel)> Confirmations { get; } = [];

    public List<TextQuestion> Questions { get; } = [];

    public List<(string Title, string Message, IReadOnlyList<string> Choices)> ChoiceQuestions { get; } = [];

    public List<string> FolderPickers { get; } = [];

    public Queue<bool> Confirm { get; } = new();

    public Queue<string?> Answers { get; } = new();

    /// <summary>
    /// The index of each choice the user makes in turn; null closes the dialog.
    /// </summary>
    public Queue<int?> Choices { get; } = new();

    public Queue<string?> Folders { get; } = new();

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken)
    {
        Messages.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, CancellationToken cancellationToken)
    {
        Confirmations.Add((title, message, confirmLabel));
        return Task.FromResult(Confirm.TryDequeue(out bool confirmed) && confirmed);
    }

    public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices, CancellationToken cancellationToken)
    {
        ChoiceQuestions.Add((title, message, choices));
        return Task.FromResult(Choices.TryDequeue(out int? choice) ? choice : null);
    }

    public Task<string?> AskTextAsync(TextQuestion question, CancellationToken cancellationToken)
    {
        Questions.Add(question);
        return Task.FromResult(Answers.TryDequeue(out string? answer) ? answer : null);
    }

    public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken)
    {
        FolderPickers.Add(title);
        return Task.FromResult(Folders.TryDequeue(out string? folder) ? folder : null);
    }
}
