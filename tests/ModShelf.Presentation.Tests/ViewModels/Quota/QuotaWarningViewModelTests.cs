using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Settings;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Quota;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Quota;

/// <summary>
/// The strip at the top while a site's requests run low: below the share the settings say, calm, then warm once they're
/// used up, with when they renew; hidden until then once dismissed; never with the warning off.
/// </summary>
public sealed class QuotaWarningViewModelTests : IDisposable
{
    // 10:00 UTC, 4 AM where the user is.
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextHour = Now.AddMinutes(45);
    // Saturday, 6 AM where the user is.
    private static readonly DateTimeOffset NextDay = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly QuotaSource _nexus = new(TestData.Nexus, "Nexus Mods");
    private readonly QuotaSource _example = new(new("example"), "Example Mods");
    private readonly InMemorySettingsService _settings = new();
    private readonly QueuedUiThread _uiThread = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingLogger<QuotaWarningViewModel> _logger = new();
    private readonly CultureScope _culture = new("en-US");
    private readonly QuotaWarningViewModel _strip;

    public QuotaWarningViewModelTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Guatemala", TimeSpan.FromHours(-6), "Guatemala", "Guatemala"));
        _strip = new QuotaWarningViewModel(new FakeSourceRegistry(_nexus, _example), _settings, _uiThread, _time, _logger);
    }

    public void Dispose()
    {
        _strip.Dispose();
        _culture.Dispose();
    }

    [Fact]
    public async Task ShowsNothingWhileEnoughIsLeftOrNothingIsKnown()
    {
        await CheckAsync();
        Assert.False(_strip.IsShown);

        _nexus.Left(50, 500, NextHour, 2000, 20000, NextDay);
        await CheckAsync();

        Assert.False(_strip.IsShown);
    }

    [Fact]
    public async Task SaysCalmlyWhenTheHoursRequestsRunLow()
    {
        _nexus.Left(42, 500, NextHour, 9000, 20000, NextDay);

        await CheckAsync();

        Assert.Equal((true, false), (_strip.IsShown, _strip.IsUsedUp));
        Assert.Equal($"Nexus Mods has 42 of its 500 requests left this hour. They renew at {Local(NextHour)}.", _strip.Message);
    }

    [Fact]
    public async Task SaysWhenTheDaysRequestsRunLowWithTheDayTheyRenew()
    {
        _nexus.Left(400, 500, NextHour, 1500, 20000, NextDay);

        await CheckAsync();

        Assert.Equal($"Nexus Mods has 1,500 of its 20,000 requests left today. They renew at {Local(NextDay)}.", _strip.Message);
        Assert.StartsWith("Sat ", Local(NextDay), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfTwoLowAllowancesSaysTheOneWithTheSmallestShareLeft()
    {
        _nexus.Left(40, 500, NextHour, 500, 20000, NextDay);

        await CheckAsync();

        Assert.StartsWith("Nexus Mods has 500 of its 20,000 requests left today.", _strip.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWarmlyWhenTheyreUsedUp()
    {
        _example.Left(0, 100, NextHour, 50, 1000, NextDay);

        await CheckAsync();

        Assert.Equal((true, true), (_strip.IsShown, _strip.IsUsedUp));
        Assert.Equal($"The requests to Example Mods are used up until {Local(NextHour)}. Checks and downloads from it wait until then.", _strip.Message);
    }

    [Fact]
    public async Task WarnsBelowTheShareTheSettingsSay()
    {
        _nexus.Left(120, 500, NextHour, 9000, 20000, NextDay);
        await CheckAsync();
        Assert.False(_strip.IsShown);

        _settings.Stored = new AppSettings { QuotaWarnBelow = 25 };
        await CheckAsync();

        Assert.True(_strip.IsShown);
    }

    [Fact]
    public async Task WithTheWarningOffShowsNothingEvenUsedUp()
    {
        _settings.Stored = new AppSettings { QuotaWarnBelow = 0 };
        _nexus.Left(0, 500, NextHour, 0, 20000, NextDay);

        await CheckAsync();

        Assert.False(_strip.IsShown);
    }

    [Fact]
    public async Task DismissedItStaysHiddenUntilTheRequestsRenew()
    {
        _nexus.Left(10, 500, NextHour, 9000, 20000, NextDay);
        await CheckAsync();

        _strip.DismissCommand.Execute(null);
        await CheckAsync();
        Assert.False(_strip.IsShown);

        _time.Advance(TimeSpan.FromMinutes(46));
        _nexus.Left(3, 500, NextHour.AddHours(1), 9000, 20000, NextDay);
        await CheckAsync();
        Assert.True(_strip.IsShown);
    }

    [Fact]
    public async Task DismissingOneSiteLeavesAnothers()
    {
        _nexus.Left(10, 500, NextHour, 9000, 20000, NextDay);
        _example.Left(5, 100, NextHour, 500, 1000, NextDay);
        await CheckAsync();
        Assert.StartsWith("Nexus Mods", _strip.Message, StringComparison.Ordinal);

        _strip.DismissCommand.Execute(null);
        await CheckAsync();

        Assert.Equal((true, $"Example Mods has 5 of its 100 requests left this hour. They renew at {Local(NextHour)}."), (_strip.IsShown, _strip.Message));
    }

    [Fact]
    public async Task AnAllowanceThatsRenewedIsntCounted()
    {
        _nexus.Left(0, 500, Now.AddMinutes(-1), 9000, 20000, NextDay);

        await CheckAsync();

        Assert.False(_strip.IsShown);
    }

    [Fact]
    public async Task LooksEveryThirtySecondsOnTheUiThreadOnceStarted()
    {
        _uiThread.Inline = false;
        _nexus.Left(10, 500, NextHour, 9000, 20000, NextDay);

        _strip.Start();
        _uiThread.RunPending();
        await _strip.Checking;
        Assert.True(_strip.IsShown);

        _nexus.Left(400, 500, NextHour, 9000, 20000, NextDay);
        _time.Advance(TimeSpan.FromSeconds(29));
        _uiThread.RunPending();
        await _strip.Checking;
        Assert.True(_strip.IsShown);

        _time.Advance(TimeSpan.FromSeconds(1));
        _uiThread.RunPending();
        await _strip.Checking;
        Assert.False(_strip.IsShown);
        Assert.Equal(TimeSpan.FromSeconds(30), QuotaWarningViewModel.CheckEvery);
    }

    [Fact]
    public async Task SettingsThatCantBeReadWarnBelowTheDefaultLoggedOnce()
    {
        _settings.ReadRefusal = new InvalidDataException("settings.json isn't JSON.");
        _nexus.Left(40, 500, NextHour, 9000, 20000, NextDay);

        await CheckAsync();
        await CheckAsync();

        Assert.True(_strip.IsShown);
        Assert.StartsWith(
            "Warning: The quota strip couldn't read the settings, so it warns below the default share System.IO.InvalidDataException: settings.json isn't JSON.",
            Assert.Single(_logger.Entries),
            StringComparison.Ordinal);
    }

    private static string Local(DateTimeOffset at)
    {
        DateTimeOffset local = at.ToOffset(TimeSpan.FromHours(-6));
        string time = local.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
        return local.Date == Now.ToOffset(TimeSpan.FromHours(-6)).Date ? time : local.ToString("ddd ", System.Globalization.CultureInfo.CurrentCulture) + time;
    }

    private Task CheckAsync()
    {
        _strip.Check();
        return _strip.Checking;
    }
}
