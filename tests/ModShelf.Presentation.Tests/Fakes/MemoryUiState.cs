using ModShelf.Application.State;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// What the app remembers, kept in memory; saving fails while the test says so, and the next change waits while the test
/// holds it.
/// </summary>
internal sealed class MemoryUiState : IUiState
{
    private readonly Lock _gate = new();
    private UiState _state = UiState.None;
    private TaskCompletionSource? _held;

    public string Location => "memory";

    /// <summary>
    /// What remembering throws while set, such as an <see cref="IOException"/>; null for nothing.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// The state remembered.
    /// </summary>
    public UiState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }

        set
        {
            lock (_gate)
            {
                _state = value;
            }
        }
    }

    /// <summary>
    /// Holds the next change until the test sets what's returned.
    /// </summary>
    public TaskCompletionSource HoldNext()
    {
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _held = held;
        }

        return held;
    }

    public Task<UiState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(State);

    public Task ChangeAsync(Func<UiState, UiState> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (Refusal is { } refusal)
        {
            return Task.FromException(refusal);
        }

        TaskCompletionSource? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
            if (held is null)
            {
                _state = change(_state);
            }
        }

        return held is null ? Task.CompletedTask : ChangeOnceReleasedAsync(held.Task, change);
    }

    private async Task ChangeOnceReleasedAsync(Task held, Func<UiState, UiState> change)
    {
        await held;
        lock (_gate)
        {
            _state = change(_state);
        }
    }
}
