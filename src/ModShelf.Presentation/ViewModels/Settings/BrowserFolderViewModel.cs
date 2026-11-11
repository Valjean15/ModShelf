using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The folder the browser saves downloads to, on Settings › Library: where ModShelf takes the file of a download waiting
/// on a site's page from (ADR 0091). The system's Downloads folder unless another is chosen.
/// </summary>
public sealed partial class BrowserFolderViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IUserFolders _folders;
    private readonly IDialogService _dialogs;
    private readonly ILogger<BrowserFolderViewModel> _logger;

    public BrowserFolderViewModel(ISettingsService settings, IUserFolders folders, IDialogService dialogs, ILogger<BrowserFolderViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _folders = folders;
        _dialogs = dialogs;
        _logger = logger;
        Folder = folders.Downloads;
    }

    /// <summary>
    /// The folder, as a full path.
    /// </summary>
    [ObservableProperty]
    public partial string Folder { get; private set; }

    /// <summary>
    /// Whether it's the system's Downloads folder, the default.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSystemsCommand))]
    public partial bool IsSystems { get; private set; } = true;

    /// <summary>
    /// Why the last choice wasn't saved; null for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// Shows the folder as the settings have it.
    /// </summary>
    internal void Show(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Folder = settings.BrowserDownloadsFolder ?? _folders.Downloads;
        IsSystems = settings.BrowserDownloadsFolder is null;
    }

    [RelayCommand]
    private async Task ChooseAsync(CancellationToken cancellationToken)
    {
        if (await _dialogs.PickFolderAsync(Strings.SettingsBrowserFolderPick, cancellationToken) is { } folder)
        {
            await SetAsync(folder, cancellationToken);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSystems))]
    private Task UseSystemsAsync(CancellationToken cancellationToken) => SetAsync(null, cancellationToken);

    private bool CanUseSystems() => !IsSystems;

    private async Task SetAsync(string? folder, CancellationToken cancellationToken)
    {
        try
        {
            await _settings.SetBrowserDownloadsFolderAsync(folder, cancellationToken);
            Show(await _settings.GetAsync(cancellationToken));
            Problem = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            LogSaveFailed(_logger, exception);
            Problem = Strings.SomethingWentWrong;
        }
    }
}
