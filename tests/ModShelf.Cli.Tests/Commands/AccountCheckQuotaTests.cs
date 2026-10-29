using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>account check nexus</c> with the real Nexus Mods source, answered by <see cref="FakeNexusApi"/>: what's left of
/// the API's allowances, as the answer to the check said.
/// </summary>
public sealed partial class AccountCheckQuotaTests : IAsyncDisposable
{
    private const int Done = 0;

    private readonly FakeNexusApi _api = new();
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private readonly InProcessCli _cli;

    public AccountCheckQuotaTests()
    {
        _cli = new InProcessCli(
            _site,
            _browser,
            configureServices: services => services
                .AddHttpClient(SourceHttpClients.NameFor(new SourceId("nexus")))
                .ConfigureHttpClient(http => http.BaseAddress = _api.BaseAddress));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task ShowsWhatsLeftOfEachAllowanceAndWhenItStartsOver()
    {
        Assert.Equal(Done, (await _cli.RunAsync(["account", "link", "nexus", "--with", "api-key"], Cancellation, FakeNexusApi.ApiKey + "\n")).ExitCode);
        _api.SetRemaining(hourly: 0, daily: 500);

        DateTimeOffset before = TimeProvider.System.GetUtcNow();
        CliResult checkedAccount = await _cli.RunAsync(["account", "check", "nexus"], Cancellation);
        DateTimeOffset after = TimeProvider.System.GetUtcNow();

        Assert.Equal(Done, checkedAccount.ExitCode);
        Assert.Empty(checkedAccount.Errors);
        string[] lines = checkedAccount.Output.TrimEnd().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Equal("Checked nexus: ModShelfPremiumTester (Premium).", lines[0]);
        Assert.Equal(["Allowance", "Left", "Of", "Starts over"], Cells(lines[1]));
        string[] daily = Cells(lines[2]);
        string[] hourly = Cells(lines[3]);
        Assert.Equal(["Per day", "499", FakeNexusApi.DailyLimit.ToString(CultureInfo.InvariantCulture)], daily[..3]);
        Assert.Equal(["Per hour", "0", FakeNexusApi.HourlyLimit.ToString(CultureInfo.InvariantCulture)], hourly[..3]);
        AssertLocalMinuteWithin(daily[3], before.AddDays(1), after.AddDays(1));
        AssertLocalMinuteWithin(hourly[3], before.AddHours(1), after.AddHours(1));
    }

    /// <summary>
    /// The time shown, a local time to the minute, is of a time between <paramref name="earliest"/> and
    /// <paramref name="latest"/>.
    /// </summary>
    private static void AssertLocalMinuteWithin(string shown, DateTimeOffset earliest, DateTimeOffset latest)
    {
        DateTime local = DateTime.ParseExact(shown, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
        DateTimeOffset start = new(local);
        Assert.InRange(earliest, start.AddMinutes(-1), start.AddMinutes(1));
        Assert.InRange(latest, start, start.AddMinutes(2));
    }

    /// <summary>
    /// A table row's cells. Cells are two or more spaces apart, so a single space stays inside a cell.
    /// </summary>
    private static string[] Cells(string row) => CellGap().Split(row.Trim());

    [GeneratedRegex(" {2,}")]
    private static partial Regex CellGap();
}
