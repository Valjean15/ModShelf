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
    /// <exception cref="InvalidDataException">
    /// The stored settings can't be read, such as after a mistake editing them, or while another program has them locked.
    /// </exception>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="settings"/> in place of the stored ones, all at once.
    /// </summary>
    /// <exception cref="InvalidDataException">The stored settings can't be read, to keep what else they hold.</exception>
    /// <exception cref="IOException">The settings can't be stored; the stored ones stay as they were.</exception>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
