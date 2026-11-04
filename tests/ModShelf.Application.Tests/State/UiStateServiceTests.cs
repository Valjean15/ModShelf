using ModShelf.Application.State;
using ModShelf.Application.Tests.Fakes;

namespace ModShelf.Application.Tests.State;

/// <summary>
/// Remembering the desktop app's state: read from the store each time, changed one change at a time, and saved only
/// when a change changes it.
/// </summary>
public sealed class UiStateServiceTests : IDisposable
{
    private readonly InMemoryUiStateStore _store = new();
    private readonly UiStateService _state;

    public UiStateServiceTests()
    {
        _state = new UiStateService(_store);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _state.Dispose();

    [Fact]
    public async Task ReadsTheStoredStateEachTime()
    {
        UiState first = await _state.GetAsync(Cancellation);
        _store.State = new UiState { Screen = "browse" };

        Assert.Same(UiState.None, first);
        Assert.Equal("browse", (await _state.GetAsync(Cancellation)).Screen);
        Assert.Equal("memory", _state.Location);
    }

    [Fact]
    public async Task ChangesTheStoredState()
    {
        _store.State = new UiState { Screen = "library", MenuCollapsed = true };

        await _state.ChangeAsync(state => state with { Screen = "browse" }, Cancellation);
        await _state.ChangeAsync(state => state.WithBrowseSearch("skyui"), Cancellation);

        Assert.Equal(new UiState { Screen = "browse", MenuCollapsed = true, BrowseSearches = ["skyui"] }, _store.State);
        Assert.Equal(2, _store.Saves);
    }

    [Fact]
    public async Task AChangeThatChangesNothingIsntSaved()
    {
        _store.State = new UiState { BrowseSearches = ["skyui"] };

        await _state.ChangeAsync(state => state.WithBrowseSearch(" "), Cancellation);
        await _state.ChangeAsync(state => state with { BrowseSearches = ["skyui"] }, Cancellation);

        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task ChangesGoOneAtATimeEachFromTheLastOnesState()
    {
        _store.Hold = new TaskCompletionSource();

        Task first = _state.ChangeAsync(state => state.WithBrowseSearch("first"), Cancellation);
        Task second = _state.ChangeAsync(state => state.WithBrowseSearch("second"), Cancellation);
        _store.Hold.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);

        Assert.Equal(["second", "first"], _store.State.BrowseSearches);
    }

    [Fact]
    public async Task AStateThatCantBeSavedStaysAsItWasAndTheNextChangeStillGoes()
    {
        _store.SaveFailure = new IOException("The disk is full.");

        await Assert.ThrowsAsync<IOException>(() => _state.ChangeAsync(state => state with { MenuCollapsed = true }, Cancellation));
        _store.SaveFailure = null;
        await _state.ChangeAsync(state => state with { Screen = "updates" }, Cancellation);

        Assert.Equal(new UiState { Screen = "updates" }, _store.State);
    }

    [Fact]
    public async Task RefusesNoChangeOrNoState()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _state.ChangeAsync(null!, Cancellation));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _state.ChangeAsync(_ => null!, Cancellation));
        Assert.Throws<ArgumentNullException>(() => new UiStateService(null!));
    }
}
