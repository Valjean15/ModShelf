using ModShelf.Application.State;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// What the app remembers, kept in memory, each change counted; saving fails while the test says so.
/// </summary>
internal sealed class MemoryUiState : IUiState
{
    private readonly Lock _gate = new();
    private UiState _state = UiState.None;

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

    public Task<UiState> GetAsync(CancellationToken cancellationToken) => Task.FromResult(State);

    public Task ChangeAsync(Func<UiState, UiState> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (Refusal is { } refusal)
        {
            return Task.FromException(refusal);
        }

        lock (_gate)
        {
            _state = change(_state);
        }

        return Task.CompletedTask;
    }
}
