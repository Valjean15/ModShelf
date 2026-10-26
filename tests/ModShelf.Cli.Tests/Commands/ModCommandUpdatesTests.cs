using System.Globalization;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

using static ModShelf.Cli.Tests.Fakes.FakeCatalogSite;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>mod updates</c> against a fake site's feed. Nexus Mods has a feed too, so every run names the fake site.
/// </summary>
public sealed class ModCommandUpdatesTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(1);

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly FakeCatalogSite _catalog = new();
    private readonly InProcessCli _cli;

    public ModCommandUpdatesTests()
    {
        _cli = new InProcessCli(_site, _browser, sources: [_catalog]);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DateTimeOffset Now => TimeProvider.System.GetUtcNow();

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ListsTheModsThatChangedInTheLastDayWithTheirLinks()
    {
        CliResult found = await UpdatesAsync();

        Assert.Equal(Done, found.ExitCode);
        Assert.Contains("3 mods of game changed since ", found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("101").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("103").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.Contains(ModLink("105").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(ModLink("102").AbsoluteUri, found.Output, StringComparison.Ordinal);
        Assert.InRange(_catalog.LastSince!.Value, Now.AddDays(-1) - Slack, Now.AddDays(-1));
        Assert.Contains("To see a mod's files: mod show", found.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(45, "1 mod of game changed since", "Its files didn't change since then, only its page")]
    [InlineData(90, "2 mods of game changed since", "None of their files changed since then, only their pages")]
    public async Task SaysWhenOnlyThePagesChanged(int minutesAgo, string heading, string footer)
    {
        CliResult found = await UpdatesAsync("--since", Iso(Now.AddMinutes(-minutesAgo)));

        Assert.Contains(heading, found.Output, StringComparison.Ordinal);
        Assert.Contains(footer, found.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("*", found.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhenEveryModHasNewFiles()
    {
        CliResult found = await UpdatesAsync("--game", "busy");

        Assert.Contains("2 mods of busy changed since", found.Output, StringComparison.Ordinal);
        Assert.Equal([true, true], Rows(found).Select(row => row.StartsWith('*')));
        Assert.Contains("* New or changed files since then." + Environment.NewLine, found.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarksTheModsWithNewFilesAndPutsTheMostRecentlyActiveFirst()
    {
        CliResult found = await UpdatesAsync("--since", "7d");

        string[] rows = Rows(found);
        Assert.Equal(["105", "103", "101", "102"], rows.Select(row => row.TrimStart('*').Trim().Split(' ')[0]));
        Assert.Equal([false, false, true, true], rows.Select(row => row.StartsWith('*')));
        Assert.Contains("* New or changed files since then. The other mods' pages changed", found.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("7d", 7)]
    [InlineData("28d", 28)]
    [InlineData("28D", 28)]
    public async Task SinceTakesDaysTheFeedsOffer(string since, int days)
    {
        CliResult found = await UpdatesAsync("--since", since);

        Assert.Equal(Done, found.ExitCode);
        Assert.InRange(_catalog.LastSince!.Value, Now.AddDays(-days) - Slack, Now.AddDays(-days));
    }

    [Fact]
    public async Task SinceTakesADateAsItsStartInLocalTime()
    {
        DateTime day = TimeProvider.System.GetLocalNow().Date.AddDays(-3);

        CliResult found = await UpdatesAsync("--since", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Assert.Equal(Done, found.ExitCode);
        Assert.Equal(new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day)), _catalog.LastSince);
    }

    [Theory]
    [InlineData("yyyy-MM-dd'T'HH:mm")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ss")]
    [InlineData("yyyy-MM-dd HH:mm")]
    [InlineData("yyyy-MM-dd HH:mm:ss")]
    public async Task SinceTakesATimeInLocalTime(string format)
    {
        DateTime moment = TimeProvider.System.GetLocalNow().Date.AddDays(-2).AddHours(18).AddMinutes(30);

        CliResult found = await UpdatesAsync("--since", moment.ToString(format, CultureInfo.InvariantCulture));

        Assert.Equal(Done, found.ExitCode);
        Assert.Equal(new DateTimeOffset(moment, TimeZoneInfo.Local.GetUtcOffset(moment)), _catalog.LastSince);
    }

    [Theory]
    [InlineData("yyyy-MM-dd'T'HH:mm'Z'")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ss'Z'")]
    [InlineData("yyyy-MM-dd'T'HH:mm:ss'+00:00'")]
    public async Task SinceTakesATimeWithItsOffset(string format)
    {
        DateTimeOffset when = new(Now.AddDays(-2).UtcDateTime.Date.AddHours(18), TimeSpan.Zero);

        CliResult found = await UpdatesAsync("--since", when.ToString(format, CultureInfo.InvariantCulture));

        Assert.Equal(Done, found.ExitCode);
        Assert.Equal(when, _catalog.LastSince);
    }

    [Fact]
    public async Task SaysWhenNothingChanged()
    {
        CliResult found = await UpdatesAsync("--since", Iso(Now.AddMinutes(-20)));

        Assert.Equal(Done, found.ExitCode);
        Assert.Contains("No mods of game changed since ", found.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BadInput, "--since takes 1d, 7d or 28d, or a date or time", "--since", "yesterday")]
    [InlineData(BadInput, "--since takes 1d, 7d or 28d, or a date or time", "--since", "2d")]
    [InlineData(BadInput, "--since takes 1d, 7d or 28d, or a date or time", "--since", "10/20/2026")]
    [InlineData(BadInput, "has no game 'elsewhere'", "--game", "elsewhere")]
    [InlineData(Failed, "couldn't list its updates: Service unavailable.", "--game", "broken")]
    [InlineData(Failed, "account link", "--game", "locked")]
    public async Task SaysWhyItCantListUpdates(int exitCode, string message, params string[] arguments)
    {
        CliResult refused = await UpdatesAsync(arguments);

        Assert.Equal(exitCode, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysHowFarBackTheSiteReaches()
    {
        string longAgo = Now.AddDays(-60).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        CliResult refused = await UpdatesAsync("--since", longAgo);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Equal("The site lists the mods updated in the last 28 days only.", refused.Errors.Trim());
    }

    [Fact]
    public async Task RefusesATimeStillToCome()
    {
        CliResult refused = await UpdatesAsync("--since", Iso(Now.AddHours(1)));

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("--since must be in the past.", refused.Errors, StringComparison.Ordinal);
        Assert.Null(_catalog.LastSince);
    }

    [Fact]
    public async Task AsksWhichSiteWhenMoreThanOneListsUpdates()
    {
        CliResult refused = await _cli.RunAsync(["mod", "updates", "--game", "game"], Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("More than one site can list updates; choose one with --source", refused.Errors, StringComparison.Ordinal);
        Assert.Contains("nexus", refused.Errors, StringComparison.Ordinal);
        Assert.Contains("fake-catalog", refused.Errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesASiteWithoutAFeed()
    {
        CliResult refused = await _cli.RunAsync(["mod", "updates", "--game", "game", "--source", "direct"], Cancellation);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("'direct' can't list updates.", refused.Errors, StringComparison.Ordinal);
    }

    private static string Iso(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

    /// <summary>
    /// The table's rows, without its heading.
    /// </summary>
    private static string[] Rows(CliResult result)
        => [.. result.Output.ReplaceLineEndings("\n").Split('\n').Where(line => line.Contains("https://catalog.example/", StringComparison.Ordinal))];

    private Task<CliResult> UpdatesAsync(params string[] arguments)
    {
        string[] defaults = arguments.Contains("--game") ? ["--source", "fake-catalog"] : ["--game", "game", "--source", "fake-catalog"];
        return _cli.RunAsync(["mod", "updates", .. arguments, .. defaults], Cancellation);
    }
}
