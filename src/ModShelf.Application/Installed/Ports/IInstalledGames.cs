namespace ModShelf.Application.Installed.Ports;

/// <summary>
/// The games installed on this computer by the stores ModShelf knows, from the stores' own files: Steam's for now. Their
/// files are only read, and nothing is sent anywhere.
/// </summary>
public interface IInstalledGames
{
    /// <summary>
    /// Reads the installed games, each store's as its files say now. A store that isn't on this computer gives none; a
    /// file that can't be read is passed over.
    /// </summary>
    Task<InstalledGames> ReadAsync(CancellationToken cancellationToken);
}
