using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.Tests.ViewModels.Accounts;
using ModShelf.Presentation.Tests.ViewModels.Settings;
using ModShelf.Presentation.ViewModels;
using ModShelf.Presentation.ViewModels.FirstRun;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.FirstRun;

/// <summary>
/// The first-run setup: for a new library only, its pages, and when it's done.
/// </summary>
public sealed class FirstRunViewModelTests
{
    private readonly SettingsHarness _settings = new();
    private readonly AccountsHarness _accounts = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly RecordingLogger<FirstRunViewModel> _logger = new();
    private readonly FirstRunViewModel _setup;

    public FirstRunViewModelTests()
    {
        _setup = new FirstRunViewModel(
            _settings.Settings,
            _settings.Library,
            _accounts.Accounts,
            _dialogs,
            [_accounts.Screen, _settings.Screen],
            _logger);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewLibraryIsShownTheSetupOverTheScreensItChanges()
    {
        await _accounts.StartAsync();

        Assert.True(await _setup.ShowIfNeededAsync(Cancellation));

        Assert.Same(_setup, Assert.Single(_dialogs.FirstRuns));
        Assert.Same(_settings.Screen, _setup.Settings);
        Assert.Same(_accounts.Screen, _setup.Accounts);
        Assert.Equal(["Information: The first-run setup is shown"], _logger.Entries.Select(entry => entry.TrimEnd()));
    }

    [Fact]
    public async Task ClosingItLeavesItForTheNextStart()
    {
        await _accounts.StartAsync();

        await _setup.ShowIfNeededAsync(Cancellation);

        Assert.Null((await _settings.Settings.GetAsync(Cancellation)).SetupDone);
        Assert.True(await _setup.ShowIfNeededAsync(Cancellation));
        Assert.Equal(2, _dialogs.FirstRuns.Count);
    }

    [Fact]
    public async Task GoesThroughItsPagesAndFinishes()
    {
        await _accounts.StartAsync();
        List<(int Page, string Step, bool Library, bool Account, bool Links, bool Back, bool Next)> seen = [];
        _dialogs.InFirstRun = async setup =>
        {
            for (int page = 0; page < FirstRunViewModel.PageCount; page++)
            {
                seen.Add((setup.Page, setup.StepText, setup.IsLibraryPage, setup.IsAccountPage, setup.IsLinksPage, setup.BackCommand.CanExecute(null), setup.NextCommand.CanExecute(null)));
                if (setup.NextCommand.CanExecute(null))
                {
                    setup.NextCommand.Execute(null);
                }
            }

            setup.BackCommand.Execute(null);
            setup.NextCommand.Execute(null);
            Assert.True(setup.IsLastPage);
            await setup.FinishCommand.ExecuteAsync(null);
            await Task.Delay(Timeout.Infinite, Cancellation);
        };

        await _setup.ShowIfNeededAsync(Cancellation);

        Assert.Equal(
            [
                (0, "Step 1 of 3", true, false, false, false, true),
                (1, "Step 2 of 3", false, true, false, true, true),
                (2, "Step 3 of 3", false, false, true, true, false),
            ],
            seen);
        Assert.True((await _settings.Settings.GetAsync(Cancellation)).SetupDone);
        Assert.Contains("Information: The first-run setup was finished", _logger.Entries.Select(entry => entry.TrimEnd()));
        Assert.False(await _setup.ShowIfNeededAsync(Cancellation));
    }

    [Fact]
    public async Task SkippingItMarksItDoneAndKeepsWhatWasChosen()
    {
        await _accounts.StartAsync();
        string chosen = Path.Combine(Path.GetTempPath(), "Chosen Mods");
        _settings.Dialogs.Folders.Enqueue(chosen);
        await _settings.StartAsync();
        _dialogs.InFirstRun = async setup =>
        {
            await setup.Settings.ChooseRootCommand.ExecuteAsync(null);
            await setup.SkipCommand.ExecuteAsync(null);
            await Task.Delay(Timeout.Infinite, Cancellation);
        };

        await _setup.ShowIfNeededAsync(Cancellation);

        Assert.True((await _settings.Settings.GetAsync(Cancellation)).SetupDone);
        Assert.Equal(chosen, (await _settings.Settings.GetAsync(Cancellation)).LibraryRoot);
        Assert.Contains("Information: The first-run setup was skipped", _logger.Entries.Select(entry => entry.TrimEnd()));
    }

    [Fact]
    public async Task ALibraryWithModsIsMarkedSetUpWithoutTheSetup()
    {
        await _accounts.StartAsync();
        _settings.Library.Add(TestData.LibraryMod(TestData.Mod("12604"), "SkyUI"));

        Assert.False(await _setup.ShowIfNeededAsync(Cancellation));

        Assert.Empty(_dialogs.FirstRuns);
        Assert.True((await _settings.Settings.GetAsync(Cancellation)).SetupDone);
        Assert.Equal(
            ["Information: The library is in use already, so it's marked set up without the first-run setup"],
            _logger.Entries.Select(entry => entry.TrimEnd()));
    }

    [Fact]
    public async Task ALinkedAccountIsALibraryInUse()
    {
        await _accounts.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));

        Assert.False(await _setup.ShowIfNeededAsync(Cancellation));

        Assert.Empty(_dialogs.FirstRuns);
        Assert.True((await _settings.Settings.GetAsync(Cancellation)).SetupDone);
    }

    [Fact]
    public async Task ASetupDoneIsntShownAgain()
    {
        await _accounts.StartAsync();
        await _settings.Settings.SetSetupDoneAsync(true, Cancellation);

        Assert.False(await _setup.ShowIfNeededAsync(Cancellation));

        Assert.Empty(_dialogs.FirstRuns);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task WhenItCantTellItShowsNothingAndLogsIt()
    {
        await _accounts.StartAsync();
        _accounts.Accounts.ListRefusal = new IOException("The database is gone.");

        Assert.False(await _setup.ShowIfNeededAsync(Cancellation));

        Assert.Empty(_dialogs.FirstRuns);
        Assert.Null((await _settings.Settings.GetAsync(Cancellation)).SetupDone);
        Assert.StartsWith(
            "Error: Telling whether the library is new, for the first-run setup, failed System.IO.IOException",
            Assert.Single(_logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASetupThatCantBeMarkedDoneSaysWhyAndStaysOpen()
    {
        await _accounts.StartAsync();
        _settings.Settings.SaveRefusal = new IOException("The disk is full.");
        bool closed = false;
        _setup.CloseRequested += (_, _) => closed = true;
        _dialogs.InFirstRun = async setup =>
        {
            setup.NextCommand.Execute(null);
            setup.NextCommand.Execute(null);
            await setup.FinishCommand.ExecuteAsync(null);
        };

        await _setup.ShowIfNeededAsync(Cancellation);

        Assert.Equal("The setup couldn't be marked done: The disk is full.", _setup.Problem);
        Assert.False(closed);
    }

    [Fact]
    public void NeedsTheSettingsAndAccountsScreens()
    {
        Assert.Throws<ArgumentException>(() => new FirstRunViewModel(
            _settings.Settings, _settings.Library, _accounts.Accounts, _dialogs, [_settings.Screen], _logger));
        Assert.Throws<ArgumentException>(() => new FirstRunViewModel(
            _settings.Settings, _settings.Library, _accounts.Accounts, _dialogs, Array.Empty<ScreenViewModel>(), _logger));
    }
}
