using Microsoft.Data.Sqlite;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// What every command does alike, in-process.
/// </summary>
public sealed class CliCommandsTests : IAsyncDisposable
{
    private const int Failed = 1;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public CliCommandsTests()
    {
        _cli = new InProcessCli(_site, _browser);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Database => Path.Combine(_cli.Folder, "library.db");

    public async ValueTask DisposeAsync()
    {
        // The database goes, with the connections kept open to it, for the harness to clean up in a new one.
        SqliteConnection.ClearAllPools();
        File.Delete(Database);
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Theory]
    [InlineData("queue", "list")]
    [InlineData("library", "list")]
    [InlineData("settings", "show")]
    [InlineData("account", "list")]
    public async Task ADatabaseThatCantBeOpenedIsNamedAndLeftAsItWas(string command, string subcommand)
    {
        byte[] notADatabase = [.. Enumerable.Repeat((byte)'x', 4096)];
        await File.WriteAllBytesAsync(Database, notADatabase, Cancellation);

        CliResult refused = await _cli.RunAsync([command, subcommand], Cancellation);

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Equal(
            $"The library database {Database} can't be opened: it isn't a ModShelf library database, or it's damaged. ModShelf leaves it as "
            + "it is: restore it from a backup, or move it elsewhere to start a new library.",
            refused.Errors.Trim());
        Assert.Equal(notADatabase, await File.ReadAllBytesAsync(Database, Cancellation));
    }
}
