namespace ModShelf.Application.State.Ports;

/// <summary>
/// Keeps the desktop app's <see cref="UiState"/> between runs. Implemented by Infrastructure.
/// </summary>
public interface IUiStateStore
{
    /// <summary>
    /// Where the state is kept, such as a file's path.
    /// </summary>
    string Location { get; }

    /// <returns>
    /// The stored state; <see cref="UiState.None"/> when none is stored yet, or what's stored can't be read, which is
    /// replaced when the state is next saved.
    /// </returns>
    Task<UiState> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="state"/> in place of the stored one, all at once, keeping what else the store holds.
    /// </summary>
    /// <exception cref="IOException">The state can't be stored; the stored one stays as it was.</exception>
    Task SaveAsync(UiState state, CancellationToken cancellationToken);
}
