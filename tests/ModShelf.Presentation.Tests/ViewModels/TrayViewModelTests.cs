using ModShelf.Application.Notifications.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Updates;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.Tests.ViewModels.Updates;
using ModShelf.Presentation.ViewModels;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The tray icon's tooltip and menu, and what closing the main window does.
/// </summary>
public sealed class TrayViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");

    private readonly UpdatesHarness _updates = new();
    private readonly InMemorySettingsService _settings = new();
    private readonly ScriptedTrayHost _tray = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly RecordingNotices _notices = new();
    private readonly RecordingLogger<TrayViewModel> _logger = new();
    private readonly TrayViewModel _viewModel;

    public TrayViewModelTests()
    {
        _viewModel = new TrayViewModel([_updates.Screen], _settings, _tray, _dialogs, _notices, _logger);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _viewModel.Dispose();
        _updates.Dispose();
    }

    [Fact]
    public async Task TheToolTipCountsTheUpdatesFound()
    {
        string before = _viewModel.ToolTip;

        await _updates.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "2"));
        string one = _viewModel.ToolTip;
        _updates.Library.Change(Address, UpdatesHarness.ModWithUpdates(Address, "Address Library", TestData.Now, "2", "3"));

        Assert.Equal("ModShelf", before);
        Assert.Equal("ModShelf: 1 update", one);
        Assert.Equal("ModShelf: 3 updates", _viewModel.ToolTip);
    }

    [Fact]
    public async Task OnceDisposedTheToolTipStopsFollowing()
    {
        _viewModel.Dispose();

        await _updates.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now, "2"));

        Assert.Equal("ModShelf", _viewModel.ToolTip);
    }

    [Fact]
    public async Task WithoutATrayClosingQuitsWithoutAsking()
    {
        _tray.IsAvailable = false;
        _settings.Stored = new AppSettings { CloseToTray = true };

        WindowClose close = await _viewModel.DecideCloseAsync(Cancellation);

        Assert.Equal(WindowClose.Quit, close);
        Assert.Empty(_dialogs.ChoiceQuestions);
        Assert.Empty(_settings.Changes);
    }

    [Theory]
    [InlineData(0, WindowClose.KeepRunningInTray, true)]
    [InlineData(1, WindowClose.Quit, false)]
    public async Task TheFirstCloseAsksAndRemembersTheAnswer(int choice, WindowClose expected, bool closeToTray)
    {
        _dialogs.Choices.Enqueue(choice);

        WindowClose first = await _viewModel.DecideCloseAsync(Cancellation);
        WindowClose second = await _viewModel.DecideCloseAsync(Cancellation);

        Assert.Equal((expected, expected), (first, second));
        (string title, string message, IReadOnlyList<string> choices) = Assert.Single(_dialogs.ChoiceQuestions);
        Assert.Equal("Keep ModShelf running?", title);
        Assert.Equal(
            "ModShelf can keep downloading and checking for updates with its window closed, from its icon in the tray. You can change this later in Settings.",
            message);
        Assert.Equal(["Keep running in the tray", "Quit ModShelf"], choices);
        Assert.Equal(closeToTray, _settings.Stored.CloseToTray);
    }

    [Fact]
    public async Task ClosingTheQuestionKeepsTheWindowOpenAndAsksAgainNextTime()
    {
        WindowClose first = await _viewModel.DecideCloseAsync(Cancellation);
        _dialogs.Choices.Enqueue(1);
        WindowClose second = await _viewModel.DecideCloseAsync(Cancellation);

        Assert.Equal((WindowClose.StayOpen, WindowClose.Quit), (first, second));
        Assert.Equal(2, _dialogs.ChoiceQuestions.Count);
        Assert.Equal(["closeToTray False"], _settings.Changes);
    }

    [Theory]
    [InlineData(true, WindowClose.KeepRunningInTray)]
    [InlineData(false, WindowClose.Quit)]
    public async Task ARememberedAnswerIsntAskedAgain(bool closeToTray, WindowClose expected)
    {
        _settings.Stored = new AppSettings { CloseToTray = closeToTray };

        WindowClose close = await _viewModel.DecideCloseAsync(Cancellation);

        Assert.Equal(expected, close);
        Assert.Empty(_dialogs.ChoiceQuestions);
        Assert.Equal(1, _tray.Asked);
    }

    [Fact]
    public async Task SettingsThatCantBeReadMeanAskingAndTheAnswerStillCounts()
    {
        _settings.ReadRefusal = new InvalidDataException("The settings in settings.json can't be read.");
        _dialogs.Choices.Enqueue(0);

        WindowClose close = await _viewModel.DecideCloseAsync(Cancellation);

        Assert.Equal(WindowClose.KeepRunningInTray, close);
        Assert.Single(_dialogs.ChoiceQuestions);
        Assert.Equal(2, _logger.Entries.Count(entry => entry.StartsWith("Warning: Couldn't read or keep what closing the window does, so it's asked", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AnAnswerThatCantBeKeptStillCounts()
    {
        _settings.SaveRefusal = new IOException("The settings couldn't be saved to settings.json: Access denied");
        _dialogs.Choices.Enqueue(1);

        WindowClose close = await _viewModel.DecideCloseAsync(Cancellation);

        Assert.Equal(WindowClose.Quit, close);
        Assert.Null(_settings.Stored.CloseToTray);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't read or keep what closing the window does", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckingFromTheTraySaysWhatTheCheckFoundInANotice()
    {
        await _updates.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _updates.Checker.Answer = new UpdateCheck(12, 3, [UpdatesHarness.Update(SkyUi, "53", TestData.Now)], []);

        await _viewModel.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(1, _updates.Checker.Checks);
        Assert.Equal([new Notification("ModShelf", "Checked 12 mods with 3 requests. 1 new update.")], _notices.Shown);
    }

    [Fact]
    public async Task ACheckThatFailsSaysWhyInTheNotice()
    {
        await _updates.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        _updates.Checker.Refusal = new InvalidDataException("The settings in settings.json can't be read.");

        await _viewModel.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal([new Notification("ModShelf", "The settings in settings.json can't be read.")], _notices.Shown);
    }

    [Fact]
    public async Task WithoutWatchedModsTheNoticeSaysSoWithoutChecking()
    {
        await _updates.StartAsync(TestData.LibraryMod(SkyUi, "SkyUI", watched: false, TestData.LibraryFile(SkyUi, "1", "a.7z")));

        await _viewModel.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(0, _updates.Checker.Checks);
        Assert.Equal([new Notification("ModShelf", Strings.UpdatesNoneWatched)], _notices.Shown);
    }

    [Fact]
    public async Task WhileACheckRunsTheNoticeSaysSo()
    {
        await _updates.StartAsync(UpdatesHarness.ModWithUpdates(SkyUi, "SkyUI", TestData.Now));
        TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _updates.Checker.Hold = hold;
        Task checking = _updates.Screen.CheckNowCommand.ExecuteAsync(null);

        await _viewModel.CheckNowCommand.ExecuteAsync(null);
        hold.SetResult();
        await checking.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.Equal(1, _updates.Checker.Checks);
        Assert.Equal([new Notification("ModShelf", "Checking the watched mods…")], _notices.Shown);
    }

    [Fact]
    public void NeedsTheUpdatesScreen()
    {
        Assert.Throws<ArgumentException>(() => new TrayViewModel([], _settings, _tray, _dialogs, _notices, _logger));
    }
}
