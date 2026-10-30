using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.ViewModels.FirstRun;

/// <summary>
/// The first-run setup of a new library, in three pages: where the library is, linking an account, and opening the
/// links websites hand downloads over with. Each page is the part of the Settings or Accounts screen it changes, so
/// what's chosen here shows there too, and can be changed there later. Finishing or skipping the setup marks it done;
/// closing it otherwise shows it again at the next start.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    /// <summary>
    /// How many pages there are.
    /// </summary>
    public const int PageCount = 3;

    private readonly ISettingsService _settings;
    private readonly IModLibrary _library;
    private readonly IAccountService _accounts;
    private readonly IDialogService _dialogs;
    private readonly ILogger<FirstRunViewModel> _logger;

    /// <param name="screens">The app's screens, the same the main window shows, among them Settings and Accounts.</param>
    public FirstRunViewModel(
        ISettingsService settings,
        IModLibrary library,
        IAccountService accounts,
        IDialogService dialogs,
        IEnumerable<ScreenViewModel> screens,
        ILogger<FirstRunViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _library = library;
        _accounts = accounts;
        _dialogs = dialogs;
        _logger = logger;
        ScreenViewModel[] all = [.. screens];
        Settings = all.OfType<SettingsViewModel>().SingleOrDefault()
            ?? throw new ArgumentException("The setup needs the Settings screen.", nameof(screens));
        Accounts = all.OfType<AccountsViewModel>().SingleOrDefault()
            ?? throw new ArgumentException("The setup needs the Accounts screen.", nameof(screens));
    }

    /// <summary>
    /// Asks to close the setup, once it's finished or skipped.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// The Settings screen, whose library folder the first page changes, and whose links the third.
    /// </summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// The Accounts screen, whose cards the second page shows.
    /// </summary>
    public AccountsViewModel Accounts { get; }

    /// <summary>
    /// The page shown, from 0.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepText), nameof(IsLibraryPage), nameof(IsAccountPage), nameof(IsLinksPage), nameof(IsLastPage))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand), nameof(NextCommand))]
    public partial int Page { get; private set; }

    /// <summary>
    /// Such as "Step 1 of 3".
    /// </summary>
    public string StepText => Text.Format(Strings.FirstRunStep, Page + 1, PageCount);

    public bool IsLibraryPage => Page == 0;

    public bool IsAccountPage => Page == 1;

    public bool IsLinksPage => Page == 2;

    public bool IsLastPage => Page == PageCount - 1;

    /// <summary>
    /// Why the setup couldn't be marked done; empty for nothing.
    /// </summary>
    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    /// <summary>
    /// Shows the setup, and completes once it's closed, when the library is new: the setup isn't done, the library has
    /// no mods, and no account is linked. A library in use is marked set up instead, without asking anything. When that
    /// can't be told, such as with settings that can't be read, which the Settings screen says, nothing is shown, and
    /// the failure is logged.
    /// </summary>
    /// <returns>Whether the setup was shown.</returns>
    public async Task<bool> ShowIfNeededAsync(CancellationToken cancellationToken)
    {
        try
        {
            if ((await _settings.GetAsync(cancellationToken)).SetupDone == true)
            {
                return false;
            }

            if ((await _library.ListAsync(cancellationToken)).Count > 0
                || (await _accounts.ListAsync(cancellationToken)).Any(site => site.Account is not null))
            {
                await _settings.SetSetupDoneAsync(true, cancellationToken);
                LogInUseAlready(_logger);
                return false;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCantTell(_logger, exception);
            return false;
        }

        Page = 0;
        Problem = string.Empty;
        LogShown(_logger);
        await _dialogs.ShowFirstRunAsync(this, cancellationToken);
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Page--;

    private bool CanGoBack() => Page > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Page++;

    private bool CanGoNext() => Page < PageCount - 1;

    /// <summary>
    /// Marks the setup done with what was chosen, and closes it.
    /// </summary>
    [RelayCommand]
    private Task FinishAsync(CancellationToken cancellationToken) => DoneAsync(skipped: false, cancellationToken);

    /// <summary>
    /// Marks the setup done without going on, and closes it. What was chosen so far stays.
    /// </summary>
    [RelayCommand]
    private Task SkipAsync(CancellationToken cancellationToken) => DoneAsync(skipped: true, cancellationToken);

    private Task DoneAsync(bool skipped, CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
            {
                await _settings.SetSetupDoneAsync(true, cancellationToken);
                LogDone(_logger, skipped ? "skipped" : "finished");
                CloseRequested?.Invoke(this, EventArgs.Empty);
            },
            exception => exception is IOException or InvalidDataException ? Text.Format(Strings.FirstRunCantSave, exception.Message) : null,
            problem => Problem = problem,
            exception => LogSaveFailed(_logger, exception),
            cancellationToken);
}
