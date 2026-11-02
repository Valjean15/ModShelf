using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Pictures;
using ModShelf.Application.Settings;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Pictures;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Pictures and mod details, on the Settings screen: the room saved pictures take and may take, with clearing them; how
/// long mods' saved details are kept; and how adult mods' pictures show. Choices are saved as they're made.
/// </summary>
public sealed partial class PicturesViewModel : ObservableObject
{
    // The sizes and ages offered, besides the one set when it's another.
    private static readonly int[] OfferedMegabytes = [256, AppSettings.DefaultPicturesMegabytes, 1024, 2048];
    private static readonly TimeSpan[] OfferedAges = [TimeSpan.FromHours(6), AppSettings.DefaultDetailsMaxAge, TimeSpan.FromDays(7)];

    private readonly ISettingsService _settings;
    private readonly ModPictures _pictures;
    private readonly ILogger<PicturesViewModel> _logger;
    private bool _showing;

    public PicturesViewModel(ISettingsService settings, ModPictures pictures, ILogger<PicturesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _pictures = pictures;
        _logger = logger;
        AdultChoices =
        [
            new(AdultPictures.Hidden, Strings.AdultPicturesHidden),
            new(AdultPictures.Blurred, Text.Format(Strings.IntervalDefault, Strings.AdultPicturesBlurred)),
            new(AdultPictures.Shown, Strings.AdultPicturesShown),
        ];
    }

    /// <summary>
    /// The most room saved pictures can take, smallest first.
    /// </summary>
    public ObservableCollection<SettingChoice<int>> SizeChoices { get; } = [];

    [ObservableProperty]
    public partial SettingChoice<int>? SelectedSize { get; set; }

    /// <summary>
    /// Such as "84 MB of 512 MB. The oldest go first when it's full."
    /// </summary>
    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>
    /// How full the room is, from 0 to 100.
    /// </summary>
    [ObservableProperty]
    public partial double UsedPercent { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial bool HasSaved { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial bool IsClearing { get; private set; }

    /// <summary>
    /// How old saved mod details may get, youngest first.
    /// </summary>
    public ObservableCollection<SettingChoice<TimeSpan>> AgeChoices { get; } = [];

    [ObservableProperty]
    public partial SettingChoice<TimeSpan>? SelectedAge { get; set; }

    /// <summary>
    /// How adult mods' pictures can show.
    /// </summary>
    public IReadOnlyList<SettingChoice<AdultPictures>> AdultChoices { get; }

    [ObservableProperty]
    public partial SettingChoice<AdultPictures>? SelectedAdult { get; set; }

    /// <summary>
    /// Why the last choice couldn't be saved, or the pictures couldn't be cleared; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// The last change, done once this is, for tests to wait on.
    /// </summary>
    internal Task Saving { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Shows the settings as stored, and the room the saved pictures take. Settings that can't be read show the
    /// defaults; the Settings screen says why.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken);
        await MeasureAsync(cancellationToken);
    }

    partial void OnSelectedSizeChanged(SettingChoice<int>? value)
    {
        if (value is not null)
        {
            Save(async token =>
            {
                await _settings.SetPicturesMegabytesAsync(value.Value == AppSettings.DefaultPicturesMegabytes ? null : value.Value, token);
                await MeasureAsync(token);
            });
        }
    }

    partial void OnSelectedAgeChanged(SettingChoice<TimeSpan>? value)
    {
        if (value is not null)
        {
            Save(token => _settings.SetDetailsMaxAgeAsync(value.Value == AppSettings.DefaultDetailsMaxAge ? null : value.Value, token));
        }
    }

    partial void OnSelectedAdultChanged(SettingChoice<AdultPictures>? value)
    {
        if (value is not null)
        {
            Save(token => _pictures.SetAdultAsync(value.Value, token));
        }
    }

    private bool CanClear() => HasSaved && !IsClearing;

    /// <summary>
    /// Deletes every saved picture. The screens download the ones they show again.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync(CancellationToken cancellationToken)
    {
        IsClearing = true;
        try
        {
            await _pictures.Cache.ClearAsync(cancellationToken);
            Problem = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Problem = Text.Format(Strings.SettingsPicturesCantClear, exception.Message);
        }
        finally
        {
            IsClearing = false;
        }

        await MeasureAsync(cancellationToken);
    }

    /// <summary>
    /// Saves a choice the user made, after the ones before it, then shows the settings as stored. Choices shown from the
    /// settings aren't saved back.
    /// </summary>
    private void Save(Func<CancellationToken, Task> change)
    {
        if (!_showing)
        {
            Saving = SaveAfterAsync(Saving, change);
        }
    }

    private async Task SaveAfterAsync(Task before, Func<CancellationToken, Task> change)
    {
        // An earlier change, which never throws.
        await before;

        try
        {
            await change(CancellationToken.None);
            Problem = null;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            Problem = exception.Message;
        }
        catch (Exception exception)
        {
            LogSaveFailed(_logger, exception);
            Problem = Strings.SomethingWentWrong;
        }

        await ReloadAsync(CancellationToken.None);
    }

    /// <summary>
    /// Shows the settings as stored, or the defaults when they can't be read.
    /// </summary>
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        AppSettings settings;
        try
        {
            settings = await _settings.GetAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            settings = new AppSettings();
        }

        _showing = true;
        try
        {
            int megabytes = settings.PicturesMegabytes ?? AppSettings.DefaultPicturesMegabytes;
            SizeChoices.SyncWith(
            [
                .. OfferedMegabytes.Append(megabytes).Distinct().Order()
                    .Select(offered => SizeChoices.FirstOrDefault(choice => choice.Value == offered) ?? SizeChoice(offered)),
            ]);
            SelectedSize = SizeChoices.First(choice => choice.Value == megabytes);

            TimeSpan age = settings.DetailsMaxAge ?? AppSettings.DefaultDetailsMaxAge;
            AgeChoices.SyncWith(
            [
                .. OfferedAges.Append(age).Distinct().Order()
                    .Select(offered => AgeChoices.FirstOrDefault(choice => choice.Value == offered) ?? AgeChoice(offered)),
            ]);
            SelectedAge = AgeChoices.First(choice => choice.Value == age);

            AdultPictures adult = settings.AdultPictures ?? AdultPictures.Blurred;
            SelectedAdult = AdultChoices.First(choice => choice.Value == adult);
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>
    /// Shows how much room the saved pictures take, of what they may take.
    /// </summary>
    private async Task MeasureAsync(CancellationToken cancellationToken)
    {
        long used;
        try
        {
            used = await _pictures.Cache.GetSizeAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder can't be read now; it says nothing rather than something wrong.
            SizeText = string.Empty;
            HasSaved = true;
            return;
        }

        long room = (SelectedSize?.Value ?? AppSettings.DefaultPicturesMegabytes) * 1024L * 1024L;
        SizeText = Text.Format(Strings.SettingsSavedPicturesSize, Sizes.Format(used), Megabytes(room / (1024 * 1024)));
        UsedPercent = Math.Clamp(100.0 * used / room, 0, 100);
        HasSaved = used > 0;
    }

    private static SettingChoice<int> SizeChoice(int megabytes)
    {
        string label = Megabytes(megabytes);
        return new SettingChoice<int>(megabytes, megabytes == AppSettings.DefaultPicturesMegabytes ? Text.Format(Strings.IntervalDefault, label) : label);
    }

    /// <summary>
    /// Such as "512 MB" or "1 GB": whole gigabytes as gigabytes.
    /// </summary>
    private static string Megabytes(long megabytes)
        => megabytes >= 1024 && megabytes % 1024 == 0
            ? Text.Format(Strings.SizeGigabytes, (megabytes / 1024).ToString(CultureInfo.CurrentCulture))
            : megabytes >= 1024
                ? Text.Format(Strings.SizeGigabytes, (megabytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture))
                : Text.Format(Strings.SizeMegabytes, megabytes.ToString(CultureInfo.CurrentCulture));

    private static SettingChoice<TimeSpan> AgeChoice(TimeSpan age)
    {
        string label = age.Ticks switch
        {
            _ when age == TimeSpan.FromDays(7) => Strings.AgeOneWeek,
            _ when age == TimeSpan.FromDays(1) => Strings.AgeOneDay,
            _ when age == TimeSpan.FromHours(1) => Strings.AgeOneHour,
            long ticks when ticks % TimeSpan.TicksPerDay == 0 => Text.Format(Strings.AgeDays, ticks / TimeSpan.TicksPerDay),
            long ticks when ticks % TimeSpan.TicksPerHour == 0 => Text.Format(Strings.AgeHours, ticks / TimeSpan.TicksPerHour),
            _ => Text.Format(Strings.AgeMinutes, (long)age.TotalMinutes),
        };
        return new SettingChoice<TimeSpan>(age, age == AppSettings.DefaultDetailsMaxAge ? Text.Format(Strings.IntervalDefault, label) : label);
    }
}
