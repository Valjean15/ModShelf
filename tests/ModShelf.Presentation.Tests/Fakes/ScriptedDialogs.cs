using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.FirstRun;
using ModShelf.Presentation.ViewModels.Settings;

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

    /// <summary>
    /// The file the user picks each time in turn; null cancels.
    /// </summary>
    public Queue<string?> Files { get; } = new();

    /// <summary>
    /// The file pickers shown: each one's title and the folder it starts in.
    /// </summary>
    public List<(string Title, string? Folder)> FilePickers { get; } = [];

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

    public Task<string?> PickFileAsync(string title, string? folder, CancellationToken cancellationToken)
    {
        FilePickers.Add((title, folder));
        return Task.FromResult(Files.TryDequeue(out string? file) ? file : null);
    }

    /// <summary>
    /// Each Add a game… shown.
    /// </summary>
    public List<AddGameViewModel> AddingGames { get; } = [];

    /// <summary>
    /// What the user does in Add a game…: whether they added a game, once it's done. By default, once the sites' games are
    /// read, they type the next of <see cref="Answers"/> and press Enter, or close it when there's none.
    /// </summary>
    public Func<AddGameViewModel, Task<bool>>? InAddGame { get; set; }

    public Task<bool> AddGameAsync(AddGameViewModel adding, CancellationToken cancellationToken)
    {
        AddingGames.Add(adding);
        return InAddGame?.Invoke(adding) ?? TypeAnswerAsync(adding);
    }

    /// <summary>
    /// Types the next answer in the picker and presses Enter, as a game's key is added.
    /// </summary>
    private async Task<bool> TypeAnswerAsync(AddGameViewModel adding)
    {
        await adding.Reading;
        if (!Answers.TryDequeue(out string? answer) || answer is null)
        {
            return false;
        }

        bool added = false;
        adding.AddRequested += (_, _) => added = true;
        adding.Picker.Query = answer;
        adding.Picker.ChooseCommand.Execute(null);
        return added;
    }

    /// <summary>
    /// Each dialog that moved a game's files, or would have.
    /// </summary>
    public List<GameMoveViewModel> MovingGames { get; } = [];

    /// <summary>
    /// What the user does in the dialog that moves a game's files. By default, once it says what would move, they press
    /// Move. Once this is done, unless the dialog closed itself, as when the files moved, the user closes it.
    /// </summary>
    public Func<GameMoveViewModel, Task>? InMoveGame { get; set; }

    public async Task MoveGameAsync(GameMoveViewModel moving, CancellationToken cancellationToken)
    {
        MovingGames.Add(moving);
        bool closed = false;
        void Close(object? sender, EventArgs e) => closed = true;
        moving.CloseRequested += Close;
        try
        {
            await (InMoveGame?.Invoke(moving) ?? PressMoveAsync(moving));
            if (!closed && moving.CloseCommand.CanExecute(null))
            {
                moving.CloseCommand.Execute(null);
            }
        }
        finally
        {
            moving.CloseRequested -= Close;
        }
    }

    /// <summary>
    /// Presses Move once the dialog says what would move, when it can.
    /// </summary>
    private static async Task PressMoveAsync(GameMoveViewModel moving)
    {
        await moving.Planning;
        if (moving.MoveCommand.CanExecute(null))
        {
            await moving.MoveCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Each first-run setup shown.
    /// </summary>
    public List<FirstRunViewModel> FirstRuns { get; } = [];

    /// <summary>
    /// What the user does while the first-run setup is open; by default they close it. The dialog closes once this is
    /// done, or as soon as the setup asks to close.
    /// </summary>
    public Func<FirstRunViewModel, Task> InFirstRun { get; set; } = _ => Task.CompletedTask;

    /// <summary>
    /// The MCP server's details each time they were shown.
    /// </summary>
    public List<AgentDetailsViewModel> AgentDetails { get; } = [];

    public Task ShowAgentDetailsAsync(AgentDetailsViewModel details, CancellationToken cancellationToken)
    {
        AgentDetails.Add(details);
        return Task.CompletedTask;
    }

    public async Task ShowFirstRunAsync(FirstRunViewModel setup, CancellationToken cancellationToken)
    {
        FirstRuns.Add(setup);
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Close(object? sender, EventArgs e) => closed.TrySetResult();
        setup.CloseRequested += Close;
        try
        {
            await Task.WhenAny(InFirstRun(setup), closed.Task);
        }
        finally
        {
            setup.CloseRequested -= Close;
        }
    }
}
