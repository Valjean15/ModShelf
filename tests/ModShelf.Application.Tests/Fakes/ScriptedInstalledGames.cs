using ModShelf.Application.Installed.Ports;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// The games installed on this computer, as the test sets them, found by Steam in a folder of the test's.
/// </summary>
internal sealed class ScriptedInstalledGames : IInstalledGames
{
    public static readonly GameStore Steam = new("steam", "Steam", Path.Combine(Path.GetTempPath(), "Steam"));

    public List<InstalledGame> Games { get; } = [];

    public int Reads { get; private set; }

    public InstalledGame Add(string id, string name)
    {
        InstalledGame game = new("steam", id, name, Path.Combine(Steam.Folder, "steamapps", "common", name));
        Games.Add(game);
        return game;
    }

    public Task<InstalledGames> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(new InstalledGames([Steam], [.. Games]));
    }
}
