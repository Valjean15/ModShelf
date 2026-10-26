using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

using static ModShelf.Cli.Tests.Fakes.FakeCatalogSite;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>mod show</c> against a fake site with a catalog.
/// </summary>
public sealed class ModCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public ModCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [new FakeCatalogSite()]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ShowsTheModAndItsCurrentFilesWithTheirLinks()
    {
        CliResult shown = await RunAsync(["mod", "show", ModLink("1").AbsoluteUri]);

        Assert.Equal(Done, shown.ExitCode);
        string[] expected =
        [
            "Example Mod",
            "Example Game",
            "Someone",
            "2.0",
            ModLink("1").AbsoluteUri,
            "Makes the example better.",
            "Adult content.",
            "Main files",
            "Optional files",
            FileLink("1", "20").AbsoluteUri,
            "3 more files",
        ];
        Assert.All(expected, text => Assert.Contains(text, shown.Output, StringComparison.Ordinal));
        Assert.DoesNotContain("Old versions", shown.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Changelog", shown.Output, StringComparison.Ordinal);
        Assert.StartsWith("*", Row(shown, FileLink("1", NewestMainFile)), StringComparison.Ordinal);
        Assert.StartsWith(" ", Row(shown, FileLink("1", "11")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListsTheNewestFileOfEachSectionFirst()
    {
        CliResult shown = await RunAsync(["mod", "show", ModLink("1").AbsoluteUri]);

        Assert.True(
            shown.Output.IndexOf(FileLink("1", NewestMainFile).AbsoluteUri, StringComparison.Ordinal)
            < shown.Output.IndexOf(FileLink("1", "11").AbsoluteUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllListsEveryFile()
    {
        CliResult shown = await RunAsync(["mod", "show", ModLink("1").AbsoluteUri, "--all"]);

        Assert.Equal(Done, shown.ExitCode);
        string[] expected = ["Old versions", "Archived files", "Files in no section", FileLink("1", "30").AbsoluteUri, FileLink("1", "50").AbsoluteUri];
        Assert.All(expected, text => Assert.Contains(text, shown.Output, StringComparison.Ordinal));
        Assert.DoesNotContain("more file", shown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALinkToAnOldFileShowsItsSection()
    {
        CliResult shown = await RunAsync(["mod", "show", FileLink("1", "30").AbsoluteUri]);

        Assert.Contains("Old versions", shown.Output, StringComparison.Ordinal);
        Assert.StartsWith("*", Row(shown, FileLink("1", "30")), StringComparison.Ordinal);
        Assert.DoesNotContain("Archived files", shown.Output, StringComparison.Ordinal);
        Assert.Contains("2 more files", shown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheChangelogListsEachVersionsChanges()
    {
        CliResult shown = await RunAsync(["mod", "show", ModLink("1").AbsoluteUri, "--changelog"]);

        string changelog = shown.Output[shown.Output.IndexOf("Changelog", StringComparison.Ordinal)..].ReplaceLineEndings("\n");
        Assert.Equal("Changelog\n1.0\n  - First release.\n2.0\n  - Faster loading.\n  - Fixed the crash on start.\n", changelog);
    }

    [Fact]
    public async Task SaysWhenTheModIsHiddenAndHasNoFiles()
    {
        CliResult shown = await RunAsync(["mod", "show", ModLink("2").AbsoluteUri]);

        Assert.Equal(Done, shown.ExitCode);
        Assert.Contains("The mod is hidden or removed", shown.Output, StringComparison.Ordinal);
        Assert.Contains("The site lists no files for this mod.", shown.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a link", BadInput, "isn't a complete link")]
    [InlineData("mailto:someone@example.com", BadInput, "No source knows this link.")]
    [InlineData("https://example.com/files/mod.zip", BadInput, "isn't to a mod on a site")]
    [InlineData("https://catalog.example/game/mods/404", BadInput, "no such mod or file")]
    [InlineData("https://catalog.example/game/mods/401", Failed, "account link")]
    public async Task SaysWhyALinkCantBeLookedUp(string link, int exitCode, string message)
    {
        CliResult refused = await RunAsync(["mod", "show", link]);

        Assert.Equal(exitCode, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
        Assert.Empty(refused.Output);
    }

    [Fact]
    public async Task DownloadsNothing()
    {
        await RunAsync(["mod", "show", ModLink("1").AbsoluteUri]);

        Assert.Contains("The queue is empty.", (await RunAsync(["queue", "list"])).Output, StringComparison.Ordinal);
    }

    private static string Row(CliResult result, Uri link)
        => Assert.Single(result.Output.ReplaceLineEndings("\n").Split('\n'), line => line.EndsWith(link.AbsoluteUri, StringComparison.Ordinal));

    private Task<CliResult> RunAsync(string[] arguments) => _cli.RunAsync(arguments, Cancellation);
}
