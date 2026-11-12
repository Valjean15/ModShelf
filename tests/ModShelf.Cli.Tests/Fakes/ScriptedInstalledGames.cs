using ModShelf.Application.Installed.Ports;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The games installed on this computer, as the test sets them: Steam's, in a folder of the test's; none, with Steam not
/// found, until the test adds some.
/// </summary>
internal sealed class ScriptedInstalledGames : IInstalledGames
{
    public static readonly GameStore Steam = new("steam", "Steam", Path.Combine(Path.GetTempPath(), "Steam"));

    /// <summary>
    /// Whether Steam is on this computer.
    /// </summary>
    public bool HasSteam { get; set; } = true;

    public List<InstalledGame> Games { get; } = [];

    public InstalledGame Add(string id, string name)
    {
        InstalledGame game = new("steam", id, name, Path.Combine(Steam.Folder, "steamapps", "common", name));
        Games.Add(game);
        return game;
    }

    public Task<InstalledGames> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(HasSteam ? new InstalledGames([Steam], [.. Games]) : InstalledGames.None);
}
