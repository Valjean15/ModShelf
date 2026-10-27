namespace ModShelf.Application.Settings.Ports;

/// <summary>
/// Keeps the settings between runs. Implemented by Infrastructure.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Where the settings are kept, such as a file's path, to tell the user.
    /// </summary>
    string Location { get; }

    /// <returns>The stored settings; the defaults when none are stored yet.</returns>
    /// <exception cref="InvalidDataException">The stored settings can't be read, such as after a mistake editing them.</exception>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="settings"/> in place of the stored ones, all at once.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
