namespace ModShelf.Application.State;

/// <summary>
/// What the desktop app remembers for the user between runs (<see cref="UiState"/>), kept apart from the settings, in a
/// file named for it, so resetting the settings leaves it. Reading it never fails: what can't be read is forgotten.
/// Changes go one at a time.
/// </summary>
public interface IUiState
{
    /// <summary>
    /// Where the state is kept, such as a file's path.
    /// </summary>
    string Location { get; }

    /// <returns>The state as remembered; <see cref="UiState.None"/> when nothing is yet, or it can't be read.</returns>
    Task<UiState> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Remembers the state <paramref name="change"/> makes of the one remembered.
    /// </summary>
    /// <exception cref="IOException">The state can't be saved; the one remembered stays.</exception>
    Task ChangeAsync(Func<UiState, UiState> change, CancellationToken cancellationToken);
}
