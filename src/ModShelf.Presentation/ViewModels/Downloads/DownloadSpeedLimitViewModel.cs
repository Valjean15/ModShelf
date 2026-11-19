using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Settings;
using ModShelf.Domain.Downloads;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// The most ModShelf's downloads take together (ADR 0105): on Settings › Library, a box that turns the limit on, its
/// speed and its unit; on the Downloads screen, a line that says it, with a switch that turns it on and off, keeping the
/// speed. One for both, so each shows what the other changed. Each change is saved at once, and a running queue takes
/// it within seconds.
/// </summary>
public sealed partial class DownloadSpeedLimitViewModel : ObservableObject
{
    private const long Kilobyte = 1024;
    private const long Megabyte = 1024 * Kilobyte;

    // The speed offered before any is set.
    private const decimal FirstAmount = 2;

    private readonly ISettingsService _settings;
    private readonly IUiThread _uiThread;
    private readonly ILogger<DownloadSpeedLimitViewModel> _logger;

    // While the settings show, so showing them doesn't save them back.
    private bool _showing;

    public DownloadSpeedLimitViewModel(ISettingsService settings, IUiThread uiThread, ILogger<DownloadSpeedLimitViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _uiThread = uiThread;
        _logger = logger;
        Units = [new(SpeedUnit.Kilobytes, Strings.SpeedUnitKilobytes), new(SpeedUnit.Megabytes, Strings.SpeedUnitMegabytes)];
        _showing = true;
        Unit = Units[1];
        Amount = FirstAmount;
        _showing = false;
    }

    /// <summary>
    /// The units the speed can be typed in.
    /// </summary>
    public IReadOnlyList<SettingChoice<SpeedUnit>> Units { get; }

    /// <summary>
    /// Whether downloads are limited.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLimited { get; set; }

    /// <summary>
    /// The speed, in <see cref="Unit"/>s a second.
    /// </summary>
    [ObservableProperty]
    public partial decimal? Amount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Minimum), nameof(Maximum), nameof(Increment))]
    public partial SettingChoice<SpeedUnit> Unit { get; set; }

    /// <summary>
    /// The least <see cref="Amount"/> can be in <see cref="Unit"/>.
    /// </summary>
    public decimal Minimum => Unit.Value == SpeedUnit.Kilobytes ? DownloadSpeed.Minimum / Kilobyte : 0.1m;

    /// <summary>
    /// The most <see cref="Amount"/> can be in <see cref="Unit"/>.
    /// </summary>
    public decimal Maximum => DownloadSpeed.Maximum / (decimal)SizeOf(Unit.Value);

    /// <summary>
    /// How much the box's arrows change <see cref="Amount"/> by: 64 KB, or 1 MB.
    /// </summary>
    public decimal Increment => Unit.Value == SpeedUnit.Kilobytes ? 64 : 1;

    /// <summary>
    /// Whether a limit is set, on or off, which the Downloads screen's line shows.
    /// </summary>
    [ObservableProperty]
    public partial bool HasLimit { get; private set; }

    /// <summary>
    /// Such as "Limited to 2.00 MB/s", or "Not limited (2.00 MB/s when on)"; empty without a limit.
    /// </summary>
    [ObservableProperty]
    public partial string StateText { get; private set; } = string.Empty;

    /// <summary>
    /// Why the last change wasn't saved; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// The last saving, for tests to wait on.
    /// </summary>
    internal Task Saving { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The limit as the box and the amount say, in bytes a second, within what it can be; null without an amount.
    /// </summary>
    private long? Bytes => Amount is > 0 and { } amount
        ? Math.Clamp((long)(amount * SizeOf(Unit.Value)) / Kilobyte * Kilobyte, DownloadSpeed.Minimum, DownloadSpeed.Maximum)
        : null;

    /// <summary>
    /// Reads the limit from the settings again, as the Downloads screen does each time it shows: it may have changed from
    /// the command line, or by an agent.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            AppSettings settings = await _settings.GetAsync(cancellationToken);
            _uiThread.Post(() => Show(settings));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogReadFailed(_logger, exception);
        }
    }

    /// <summary>
    /// Shows the limit as the settings have it, without saving it back: in whole megabytes when it's a whole number of
    /// them, otherwise in kilobytes.
    /// </summary>
    internal void Show(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _showing = true;
        try
        {
            IsLimited = settings.DownloadSpeedCap is not null;
            HasLimit = settings.DownloadSpeedLimit is not null;
            if (settings.DownloadSpeedLimit is { } limit)
            {
                Unit = Units[limit % Megabyte == 0 ? 1 : 0];
                Amount = limit / (decimal)SizeOf(Unit.Value);
            }

            ShowState(settings.DownloadSpeedLimit);
        }
        finally
        {
            _showing = false;
        }
    }

    private static long SizeOf(SpeedUnit unit) => unit == SpeedUnit.Kilobytes ? Kilobyte : Megabyte;

    partial void OnIsLimitedChanged(bool value) => Save();

    partial void OnAmountChanged(decimal? value) => Save();

    /// <summary>
    /// Keeps the speed as it was in the other unit, within what it can be there.
    /// </summary>
    partial void OnUnitChanged(SettingChoice<SpeedUnit> oldValue, SettingChoice<SpeedUnit> newValue)
    {
        if (_showing || oldValue is null || Amount is not { } amount)
        {
            return;
        }

        _showing = true;
        try
        {
            decimal converted = Math.Round(amount * SizeOf(oldValue.Value) / SizeOf(newValue.Value), 1);
            Amount = Math.Clamp(converted, Minimum, Maximum);
        }
        finally
        {
            _showing = false;
        }

        Save();
    }

    private void Save()
    {
        if (!_showing && Bytes is { } bytes)
        {
            Saving = SaveAsync(bytes, IsLimited);
        }
    }

    private async Task SaveAsync(long bytes, bool applies)
    {
        try
        {
            await _settings.SetDownloadSpeedLimitAsync(bytes, applies, CancellationToken.None);
            _uiThread.Post(() =>
            {
                HasLimit = true;
                ShowState(bytes);
                Problem = null;
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            LogSaveFailed(_logger, exception);
            _uiThread.Post(() => Problem = Strings.SomethingWentWrong);
        }
    }

    private void ShowState(long? limit)
        => StateText = limit is not { } bytes ? string.Empty
            : Text.Format(IsLimited ? Strings.DownloadsSpeedLimited : Strings.DownloadsSpeedNotLimited, Sizes.Format(bytes));
}
