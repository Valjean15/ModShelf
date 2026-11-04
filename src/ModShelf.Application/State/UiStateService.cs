using ModShelf.Application.State.Ports;

namespace ModShelf.Application.State;

/// <summary>
/// Reads the state from the <see cref="IUiStateStore"/> each time, and changes it one change at a time.
/// </summary>
internal sealed class UiStateService : IUiState, IDisposable
{
    private readonly IUiStateStore _store;
    private readonly SemaphoreSlim _changing = new(1, 1);

    public UiStateService(IUiStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    public string Location => _store.Location;

    public Task<UiState> GetAsync(CancellationToken cancellationToken) => _store.LoadAsync(cancellationToken);

    public async Task ChangeAsync(Func<UiState, UiState> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        await _changing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UiState state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            UiState changed = change(state) ?? throw new InvalidOperationException("The changed state can't be null.");
            if (changed != state)
            {
                await _store.SaveAsync(changed, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _changing.Release();
        }
    }

    public void Dispose() => _changing.Dispose();
}
