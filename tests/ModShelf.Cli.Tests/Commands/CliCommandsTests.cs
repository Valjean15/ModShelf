using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// What every command does alike, in-process.
/// </summary>
public sealed class CliCommandsTests : IAsyncDisposable
{
    private const int Failed = 1;
    private const int BadInput = 2;
    private const int Unexpected = 70;
    private const int Interrupted = 130;

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

    [Theory]
    [InlineData("queue", "list", "--everything")]
    [InlineData("library", "lists")]
    [InlineData("library", "move")]
    public async Task ArgumentsTheCommandLineCantReadAreBadInput(params string[] arguments)
    {
        CliResult refused = await _cli.RunAsync(arguments, Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.NotEmpty(refused.Errors);
    }

    [Fact]
    public async Task SomethingUnexpectedIsSaidInALineWithTheDetailsInTheLog()
    {
        await using InProcessCli cli = Broken(_ => throw new NotSupportedException("Settings of a kind no one wrote"));

        CliResult stopped = await cli.RunAsync(["settings", "show"], Cancellation);

        Assert.Equal(Unexpected, stopped.ExitCode);
        Assert.Equal(
            $"Something went wrong that ModShelf didn't expect, so it stopped: Settings of a kind no one wrote The details are in the log in {Path.Combine(cli.Folder, "logs")}.",
            stopped.Errors.Trim());
        string logged = Assert.Single(cli.Logs.Entries, entry => entry.Contains("didn't expect", StringComparison.Ordinal));
        Assert.Contains("The command settings show stopped on something it didn't expect", logged, StringComparison.Ordinal);
        Assert.Contains("NotSupportedException", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CtrlCStopsACommandThatDoesntSayItself()
    {
        TaskCompletionSource loading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using InProcessCli cli = Broken(async cancellationToken =>
        {
            loading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Never reached.");
        });
        await using CliRun run = cli.Start(["settings", "show"], TextReader.Null);
        await loading.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        await run.PressCtrlCAsync();
        CliResult stopped = await run.WaitForExitAsync(Cancellation);

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Equal("Stopped.", stopped.Errors.Trim());
    }

    /// <summary>
    /// The CLI with settings that load as <paramref name="load"/> says.
    /// </summary>
    private InProcessCli Broken(Func<CancellationToken, Task<AppSettings>> load)
        => new(_site, _browser, configureServices: services =>
        {
            services.RemoveAll<ISettingsStore>();
            services.AddSingleton<ISettingsStore>(new ScriptedSettingsStore(load));
        });
}
