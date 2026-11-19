using ModShelf.Application.Settings;
using ModShelf.Domain.Downloads;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

/// <summary>
/// The download speed limit's box on Settings › Library and its line on the Downloads screen (ADR 0105).
/// </summary>
public sealed class DownloadSpeedLimitViewModelTests
{
    private const long Megabyte = 1024 * 1024;

    private readonly InMemorySettingsService _settings = new();
    private readonly QueuedUiThread _uiThread = new();
    private readonly RecordingLogger<DownloadSpeedLimitViewModel> _logger = new();
    private readonly DownloadSpeedLimitViewModel _limit;

    public DownloadSpeedLimitViewModelTests()
    {
        _limit = new DownloadSpeedLimitViewModel(_settings, _uiThread, _logger);
    }

    [Fact]
    public void OffersTwoMegabytesASecondUntilALimitIsSet()
    {
        Assert.Equal((false, false, 2m, SpeedUnit.Megabytes, string.Empty), (_limit.IsLimited, _limit.HasLimit, _limit.Amount, _limit.Unit.Value, _limit.StateText));
        Assert.Equal(["KB/s", "MB/s"], _limit.Units.Select(unit => unit.Label));
        Assert.Empty(_settings.Changes);
    }

    [Fact]
    public void ShowsALimitInWholeMegabytesOrElseInKilobytesWithoutSavingItBack()
    {
        _limit.Show(new AppSettings { DownloadSpeedLimit = 3 * Megabyte });
        var megabytes = (_limit.IsLimited, _limit.HasLimit, _limit.Amount, _limit.Unit.Value, _limit.StateText);

        _limit.Show(new AppSettings { DownloadSpeedLimit = 512 * 1024, DownloadSpeedLimitOn = false });
        var kilobytes = (_limit.IsLimited, _limit.HasLimit, _limit.Amount, _limit.Unit.Value, _limit.StateText);

        _limit.Show(new AppSettings());

        Assert.Equal((true, true, 3m, SpeedUnit.Megabytes, "Limited to 3.00 MB/s"), megabytes);
        Assert.Equal((false, true, 512m, SpeedUnit.Kilobytes, "Not limited (512 KB/s when on)"), kilobytes);
        Assert.Equal((false, false, string.Empty), (_limit.IsLimited, _limit.HasLimit, _limit.StateText));
        Assert.Empty(_settings.Changes);
    }

    [Fact]
    public async Task TurningTheLimitOnSavesTheSpeedShownAndTheLineSaysIt()
    {
        _limit.IsLimited = true;
        await _limit.Saving;

        Assert.Equal(["downloadSpeedLimit 2097152 on"], _settings.Changes);
        Assert.Equal((true, "Limited to 2.00 MB/s"), (_limit.HasLimit, _limit.StateText));
        Assert.Equal(2 * Megabyte, _settings.Stored.DownloadSpeedCap);
    }

    [Fact]
    public async Task TurningTheLimitOffKeepsItsSpeed()
    {
        _settings.Stored = new AppSettings { DownloadSpeedLimit = 5 * Megabyte };
        _limit.Show(_settings.Stored);

        _limit.IsLimited = false;
        await _limit.Saving;

        Assert.Equal(["downloadSpeedLimit 5242880 off"], _settings.Changes);
        Assert.Equal((true, "Not limited (5.00 MB/s when on)"), (_limit.HasLimit, _limit.StateText));
        Assert.Equal((5 * Megabyte, (long?)null), (_settings.Stored.DownloadSpeedLimit, _settings.Stored.DownloadSpeedCap));
    }

    [Fact]
    public async Task ChangingTheSpeedSavesItInWholeKilobytesAndAnEmptyBoxSavesNothing()
    {
        _limit.Show(new AppSettings { DownloadSpeedLimit = 2 * Megabyte });

        _limit.Amount = 1.5m;
        await _limit.Saving;
        _limit.Amount = null;
        await _limit.Saving;

        Assert.Equal(["downloadSpeedLimit 1572864 on"], _settings.Changes);
        Assert.Equal("Limited to 1.50 MB/s", _limit.StateText);
    }

    [Theory]
    [InlineData(2, SpeedUnit.Megabytes, SpeedUnit.Kilobytes, 2048)]
    [InlineData(512, SpeedUnit.Kilobytes, SpeedUnit.Megabytes, 0.5)]
    [InlineData(1000, SpeedUnit.Kilobytes, SpeedUnit.Megabytes, 1)]
    [InlineData(16, SpeedUnit.Kilobytes, SpeedUnit.Megabytes, 0.1)]
    public async Task ChangingTheUnitKeepsTheSpeedWithinWhatTheUnitAllows(double amount, SpeedUnit from, SpeedUnit to, double expected)
    {
        _limit.Show(new AppSettings { DownloadSpeedLimit = 4 * Megabyte });
        _limit.Unit = _limit.Units.Single(unit => unit.Value == from);
        _limit.Amount = (decimal)amount;
        await _limit.Saving;
        int saved = _settings.Changes.Count;

        _limit.Unit = _limit.Units.Single(unit => unit.Value == to);
        await _limit.Saving;

        Assert.Equal((decimal)expected, _limit.Amount);
        Assert.Equal(saved + 1, _settings.Changes.Count);
        Assert.InRange(_settings.Stored.DownloadSpeedLimit!.Value, DownloadSpeed.Minimum, DownloadSpeed.Maximum);
    }

    [Fact]
    public void TheBoxAllowsFrom16KilobytesTo10000MegabytesASecondItsArrowsStepping64KilobytesOr1Megabyte()
    {
        _limit.Unit = _limit.Units[0];
        var kilobytes = (_limit.Minimum, _limit.Maximum, _limit.Increment);
        _limit.Unit = _limit.Units[1];

        Assert.Equal((16m, 10_240_000m, 64m), kilobytes);
        Assert.Equal((0.1m, 10_000m, 1m), (_limit.Minimum, _limit.Maximum, _limit.Increment));
    }

    [Fact]
    public async Task SaysWhenTheLimitCantBeSaved()
    {
        _settings.SaveRefusal = new IOException("The disk is full.");

        _limit.IsLimited = true;
        await _limit.Saving;

        Assert.Equal("Something went wrong. The log has the details.", _limit.Problem);
        Assert.False(_limit.HasLimit);
        Assert.Contains(_logger.Entries, entry => entry.Contains("Couldn't save the download speed limit", StringComparison.Ordinal));

        _settings.SaveRefusal = null;
        _limit.Amount = 3;
        await _limit.Saving;

        Assert.Null(_limit.Problem);
    }

    [Fact]
    public async Task ReadsTheLimitAgainOnTheUiThreadAndKeepsItWhenTheSettingsCantBeRead()
    {
        _uiThread.Inline = false;
        _settings.Stored = new AppSettings { DownloadSpeedLimit = 256 * 1024 };

        await _limit.RefreshAsync(TestContext.Current.CancellationToken);
        bool before = _limit.HasLimit;
        _uiThread.RunPending();
        _settings.ReadRefusal = new InvalidDataException("settings.json isn't JSON.");
        await _limit.RefreshAsync(TestContext.Current.CancellationToken);
        _uiThread.RunPending();

        Assert.False(before);
        Assert.Equal((true, 256m, SpeedUnit.Kilobytes, "Limited to 256 KB/s"), (_limit.IsLimited, _limit.Amount, _limit.Unit.Value, _limit.StateText));
        Assert.Contains(_logger.Entries, entry => entry.Contains("Couldn't read the download speed limit", StringComparison.Ordinal));
        Assert.Empty(_settings.Changes);
    }
}
