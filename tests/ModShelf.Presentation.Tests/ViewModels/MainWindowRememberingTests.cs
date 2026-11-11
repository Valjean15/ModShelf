using ModShelf.Application.State;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.Tests.ViewModels.QuickSearch;
using ModShelf.Presentation.ViewModels;
using ModShelf.Presentation.ViewModels.Accounts;
using ModShelf.Presentation.ViewModels.Quota;
using ModShelf.Presentation.ViewModels.Undo;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// The main window comes back as it was left: the sidebar open or collapsed and the screen last open, shown without being
/// remembered again; and it remembers them as they change, with the window's place as the view tells it.
/// </summary>
public sealed class MainWindowRememberingTests : IDisposable
{
    private static readonly WindowPlacement Placement = new(120, 80, 1280, 800, maximized: false);

    private readonly NavigationService _navigation = new();
    private readonly QueuedUiThread _uiThread = new();
    private readonly RecordingLogger<MainWindowViewModel> _logger = new();
    private readonly MemoryUiState _uiState = new();
    private readonly MainWindowViewModel _window;

    public MainWindowRememberingTests()
    {
        _window = new MainWindowViewModel(
            _navigation,
            _uiThread,
            TestScreen.OneOfEach(),
            new QuickSearchHarness(_navigation).Search,
            new UndoBarViewModel(new InMemoryModLibrary(), _uiThread, TimeProvider.System, new RecordingLogger<UndoBarViewModel>()),
            new QuotaWarningViewModel(new FakeSourceRegistry(), new InMemorySettingsService(), _uiThread, TimeProvider.System, new RecordingLogger<QuotaWarningViewModel>()),
            _uiState,
            _logger);
    }

    public void Dispose() => _window.Dispose();

    [Fact]
    public void ANewWindowRemembersNothing()
    {
        Assert.Equal(UiState.None, _uiState.State);
        Assert.Equal((true, Screen.Library), (_window.IsSidebarOpen, _window.CurrentScreen.Screen));
    }

    [Fact]
    public void ComesBackAsItWasLeftWithoutRememberingItAgain()
    {
        _window.Restore(UiState.None with { MenuCollapsed = true, Screen = "downloads" });

        Assert.Equal((false, Screen.Downloads, Screen.Downloads), (_window.IsSidebarOpen, _window.CurrentScreen.Screen, _navigation.Current));
        Assert.Equal(1, ((TestScreen)_window.CurrentScreen).Shows);
        Assert.Equal(UiState.None, _uiState.State);
    }

    [Fact]
    public void ComesBackAtOnceWhileTheUiThreadIsBusy()
    {
        _uiThread.Inline = false;

        _window.Restore(UiState.None with { Screen = "Updates" });

        Assert.Equal(Screen.Updates, _window.CurrentScreen.Screen);
        _uiThread.RunPending();
        Assert.Equal(Screen.Updates, _window.CurrentScreen.Screen);
        Assert.Equal(UiState.None, _uiState.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Collections")]
    [InlineData("42")]
    public void AScreenItDoesntKnowIsPassedOver(string? screen)
    {
        _window.Restore(UiState.None with { MenuCollapsed = false, Screen = screen });

        Assert.Equal((true, Screen.Library), (_window.IsSidebarOpen, _window.CurrentScreen.Screen));
    }

    [Fact]
    public async Task RemembersTheSidebarAndTheScreenAsTheyChange()
    {
        _window.Restore(UiState.None);

        _window.ToggleSidebarCommand.Execute(null);
        _window.CurrentScreen = _window.Screens.Single(screen => screen.Screen == Screen.Browse);
        await _window.Remembering;

        Assert.Equal((true, "Browse"), (_uiState.State.MenuCollapsed, _uiState.State.Screen));

        _window.ToggleSidebarCommand.Execute(null);
        _navigation.NavigateTo(Screen.Settings);
        _uiThread.RunPending();
        await _window.Remembering;

        Assert.Equal((false, "Settings"), (_uiState.State.MenuCollapsed, _uiState.State.Screen));
    }

    [Fact]
    public async Task RemembersTheWindowsPlaceAsTheViewTellsIt()
    {
        _window.RememberWindow(Placement);
        _window.RememberWindow(new WindowPlacement(120, 80, 1280, 800, maximized: true));
        await _window.Remembering;

        Assert.Equal(new WindowPlacement(120, 80, 1280, 800, maximized: true), _uiState.State.Window);
    }

    [Fact]
    public async Task ChangesAreRememberedOneAfterAnother()
    {
        TaskCompletionSource held = _uiState.HoldNext();
        _window.RememberWindow(Placement);
        _window.ToggleSidebarCommand.Execute(null);

        Assert.False(_window.Remembering.IsCompleted);
        held.SetResult();
        await _window.Remembering;
        Assert.Equal((Placement, true), (_uiState.State.Window, _uiState.State.MenuCollapsed));
    }

    [Fact]
    public async Task WhatCantBeRememberedIsLoggedAndTheNextChangeStillIs()
    {
        _uiState.Refusal = new IOException("The disk is full.");
        _window.RememberWindow(Placement);
        await _window.Remembering;
        _uiState.Refusal = null;

        _window.ToggleSidebarCommand.Execute(null);
        await _window.Remembering;

        Assert.StartsWith(
            "Warning: Couldn't remember the window, the sidebar or the screen last open System.IO.IOException: The disk is full.",
            Assert.Single(_logger.Entries),
            StringComparison.Ordinal);
        Assert.Equal(((WindowPlacement?)null, true), (_uiState.State.Window, _uiState.State.MenuCollapsed));
    }
}
