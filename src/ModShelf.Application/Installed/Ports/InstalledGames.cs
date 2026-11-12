namespace ModShelf.Application.Installed.Ports;

/// <summary>
/// The stores found on this computer, and the games installed from them, each once.
/// </summary>
public sealed record InstalledGames(IReadOnlyList<GameStore> Stores, IReadOnlyList<InstalledGame> Games)
{
    /// <summary>
    /// No store found, and no game.
    /// </summary>
    public static InstalledGames None { get; } = new([], []);
}
