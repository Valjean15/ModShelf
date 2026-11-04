using ModShelf.Application.State;
using ModShelf.Application.State.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps the UI state in memory, and counts how often it's saved. The test can make saving fail, or hold each load
/// until it lets it go.
/// </summary>
internal sealed class InMemoryUiStateStore : IUiStateStore
{
    public UiState State { get; set; } = UiState.None;

    public int Saves { get; private set; }

    /// <summary>
    /// What saving throws, while set; the state stays as it was.
    /// </summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>
    /// While set, each load waits for it.
    /// </summary>
    public TaskCompletionSource? Hold { get; set; }

    public string Location => "memory";

    public async Task<UiState> LoadAsync(CancellationToken cancellationToken)
    {
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return State;
    }

    public Task SaveAsync(UiState state, CancellationToken cancellationToken)
    {
        if (SaveFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        State = state;
        Saves++;
        return Task.CompletedTask;
    }
}
