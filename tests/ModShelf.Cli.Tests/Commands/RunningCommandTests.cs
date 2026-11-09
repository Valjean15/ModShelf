using System.CommandLine;
using System.Diagnostics;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;

using ModShelf.Cli.Commands;
using ModShelf.Cli.Hosting;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// The first line of each run's logs says what it runs: the command and the options given, never a value, the process,
/// and the process that started it, such as an agent's app (ADR 0081).
/// </summary>
public sealed class RunningCommandTests : IAsyncDisposable
{
    private static readonly CliEnvironment ParseOnly = new(
        TextReader.Null,
        TextWriter.Null,
        TextWriter.Null,
        InputIsTerminal: false,
        OutputIsTerminal: false,
        _ => throw new InvalidOperationException("Parsing builds no host."));

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly TempFolder _folder = new();
    private InProcessCli? _cli;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private InProcessCli Cli => _cli ??= new InProcessCli(_site, _browser);

    public async ValueTask DisposeAsync()
    {
        if (_cli is not null)
        {
            await _cli.DisposeAsync();
        }

        _browser.Dispose();
        await _site.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    [Theory]
    [InlineData(new[] { "mcp" }, "mcp")]
    [InlineData(new[] { "mcp", "--http", "--port", "0", "--parent", "4100" }, "mcp --http --port --parent")]
    [InlineData(new[] { "library", "rescan" }, "library rescan")]
    [InlineData(new[] { "settings", "set", "agents.port", "50000" }, "settings set")]
    [InlineData(new[] { "download", "nxm://stardewvalley/mods/2400/files/1?key=SECRET&expires=1", "--to", "/mods/secret folder" }, "download --to")]
    public void ItsTheCommandsWordsAndTheOptionsGivenAlone(string[] arguments, string typed)
    {
        ParseResult parsed = CliCommands.Create(ParseOnly).Parse(arguments);

        Assert.Equal(typed, RunningCommand.Of(parsed));
    }

    [Fact]
    public void OutsideACommandItsTheCommandLine() => Assert.Equal("the command line", RunningCommand.Description);

    [Fact]
    public async Task ACommandsLogsStartWithIt()
    {
        CliResult shown = await Cli.RunAsync(["settings", "show"], Cancellation);

        Assert.Equal(0, shown.ExitCode);
        string first = Cli.Logs.Entries.First(entry => entry.StartsWith("[ModShelf.", StringComparison.Ordinal));
        Assert.StartsWith("[ModShelf.Platform.Diagnostics.ProcessStartLog] ModShelf ", first, StringComparison.Ordinal);
        Assert.Contains($": the command line, settings show, process {Environment.ProcessId}, started by ", first, StringComparison.Ordinal);

        // Said before the library database is opened, so even when it can't be.
        Assert.Contains(Cli.Logs.Entries.SkipWhile(entry => entry != first), entry => entry.Contains("Updating the library database", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ItNamesTheProcessThatStartedIt()
    {
        using CliProcess process = CliProcess.Start(Path.Combine(_folder.Path, "library.db"), ["settings", "show"]);
        CliResult shown = await process.WaitForExitAsync(TimeSpan.FromSeconds(60), Cancellation);

        Assert.Equal(0, shown.ExitCode);
        string log = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(Path.Combine(_folder.Path, "logs"), "cli-*.log")), Cancellation);
        using Process self = Process.GetCurrentProcess();
        Assert.Matches(
            $@"ProcessStartLog\[1307\]: ModShelf \S+.*: the command line, settings show, process \d+, started by {Regex.Escape(self.ProcessName)} \(process {Environment.ProcessId}\)",
            log);
    }
}
