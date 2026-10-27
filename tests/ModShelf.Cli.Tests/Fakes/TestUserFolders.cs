using ModShelf.Application.Settings.Ports;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A Downloads folder of the test's own, in place of the user's.
/// </summary>
internal sealed class TestUserFolders(string downloads) : IUserFolders
{
    public string Downloads { get; } = downloads;
}
