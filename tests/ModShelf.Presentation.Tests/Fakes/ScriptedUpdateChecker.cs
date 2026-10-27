using ModShelf.Application.Updates;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// An update checker that answers as the test says, and can be held mid-check.
/// </summary>
internal sealed class ScriptedUpdateChecker : IUpdateChecker
{
    public int Checks { get; private set; }

    /// <summary>
    /// What a check returns.
    /// </summary>
    public UpdateCheck Answer { get; set; } = new(0, 0, [], []);

    /// <summary>
    /// What a check throws instead; null for nothing.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// Runs during a check, before it answers, such as changing the library as a check does.
    /// </summary>
    public Action? During { get; set; }

    /// <summary>
    /// When set, a check waits for it before answering.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        Checks++;
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        During?.Invoke();
        return Refusal is null ? Answer : throw Refusal;
    }
}
