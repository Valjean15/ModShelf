using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>diagnostics</c>: the report a bug report needs, as the desktop app copies it.
/// </summary>
public sealed class DiagnosticsCommandTests : IAsyncDisposable
{
    private const int Done = 0;

    private static readonly string KeySource = FakeApiKeySource.SourceId.Value;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public DiagnosticsCommandTests()
    {
        _cli = new InProcessCli(_site, _browser);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task WritesTheReport()
    {
        CliResult written = await _cli.RunAsync(["diagnostics"], Cancellation);

        Assert.Equal(Done, written.ExitCode);
        string[] lines = written.Output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Matches(@"^ModShelf \d+\.\d+\.\d+", lines[0]);
        Assert.Equal(
            ["ModShelf", "Written", "System", "Folders", "Settings", "Library", "Downloads", "Accounts"],
            lines.Select(line => line.Split([' ', ':'], 2)[0]));
        // The test's folder, under the home folder as a tilde where the temporary folder is in it, as on Windows.
        Assert.Contains($"{Path.GetFileName(_cli.Folder)}, logs ", written.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysHowAnAccountIsLinkedButNotWhose()
    {
        Assert.Equal(Done, (await _cli.RunAsync(["account", "link", KeySource], Cancellation, _site.ApiKey + "\n")).ExitCode);

        CliResult written = await _cli.RunAsync(["diagnostics"], Cancellation);

        Assert.Contains($"{KeySource} linked (ApiKey, {FakeAccountSite.KeyTier})", written.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeAccountSite.KeyUser, written.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(_site.ApiKey, written.Output, StringComparison.Ordinal);
    }
}
