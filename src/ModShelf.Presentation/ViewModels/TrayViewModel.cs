using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Desktop.Ports;
using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Settings;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// The tray icon, and what closing the main window does. The icon's tooltip counts the updates found; its menu checks
/// the watched mods now, and says what the check found in a notice. The first time the window closes, it asks whether to
/// keep running in the tray, and remembers the answer; with no tray to run in, closing quits.
/// </summary>
public sealed partial class TrayViewModel : ObservableObject, IDisposable
{
    private readonly UpdatesViewModel _updates;
    private readonly ISettingsService _settings;
    private readonly ITrayHost _tray;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notices;
    private readonly ILogger<TrayViewModel> _logger;

    /// <param name="screens">The app's screens, of which the tray uses Updates.</param>
    public TrayViewModel(
        IEnumerable<ScreenViewModel> screens,
        ISettingsService settings,
        ITrayHost tray,
        IDialogService dialogs,
        INotificationService notices,
        ILogger<TrayViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(logger);

        _updates = screens.OfType<UpdatesViewModel>().SingleOrDefault()
            ?? throw new ArgumentException("The tray needs the Updates screen.", nameof(screens));
        _settings = settings;
        _tray = tray;
        _dialogs = dialogs;
        _notices = notices;
        _logger = logger;
        ToolTip = ToolTipFor(_updates.Badge);
        _updates.PropertyChanged += OnUpdatesChanged;
    }

    /// <summary>
    /// Such as "ModShelf: 3 updates", or "ModShelf" without any.
    /// </summary>
    [ObservableProperty]
    public partial string ToolTip { get; private set; }

    /// <summary>
    /// What closing the main window should do: keep running in the tray or quit, as the settings say, after asking the
    /// first time; or nothing, when the user closes the question without answering.
    /// </summary>
    public async Task<WindowClose> DecideCloseAsync(CancellationToken cancellationToken)
    {
        if (!await _tray.IsAvailableAsync(cancellationToken))
        {
            return WindowClose.Quit;
        }

        bool? closeToTray;
        try
        {
            closeToTray = (await _settings.GetAsync(cancellationToken)).CloseToTray;
        }
        catch (InvalidDataException exception)
        {
            // The Settings screen says what's wrong with them; meanwhile, ask.
            LogCantRemember(_logger, exception);
            closeToTray = null;
        }

        if (closeToTray is { } remembered)
        {
            return remembered ? WindowClose.KeepRunningInTray : WindowClose.Quit;
        }

        int? choice = await _dialogs.ChooseAsync(
            Strings.CloseTitle,
            Strings.CloseMessage,
            [Strings.CloseKeepRunning, Strings.QuitModShelf],
            cancellationToken);
        if (choice is null)
        {
            return WindowClose.StayOpen;
        }

        bool keepRunning = choice == 0;
        try
        {
            await _settings.SetCloseToTrayAsync(keepRunning, CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // It asks again the next time; the answer counts this time.
            LogCantRemember(_logger, exception);
        }

        return keepRunning ? WindowClose.KeepRunningInTray : WindowClose.Quit;
    }

    public void Dispose() => _updates.PropertyChanged -= OnUpdatesChanged;

    private static string ToolTipFor(int updates)
        => Counts.Of(updates, Strings.AppTitle, Strings.TrayToolTipOneUpdate, Strings.TrayToolTipUpdates);

    /// <summary>
    /// Checks the watched mods now, as the Updates screen's Check now does, and says in a notice what the check found:
    /// the window may well be closed.
    /// </summary>
    [RelayCommand]
    private async Task CheckNowAsync(CancellationToken cancellationToken)
    {
        string found;
        if (_updates.CheckNowCommand.CanExecute(null))
        {
            await _updates.CheckNowCommand.ExecuteAsync(null);
            found = _updates.CheckResult ?? Strings.SomethingWentWrong;
        }
        else
        {
            found = _updates.IsChecking ? Strings.UpdatesChecking : Strings.UpdatesNoneWatched;
        }

        await _notices.ShowAsync(new Notification(Strings.AppTitle, found), cancellationToken);
    }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScreenViewModel.Badge))
        {
            ToolTip = ToolTipFor(_updates.Badge);
        }
    }
}
