using Microsoft.Extensions.Time.Testing;

using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Undo;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Undo;

/// <summary>
/// The undo bar: what a change did, with Undo, for ten seconds, one at a time; undoing through the library, and saying
/// why it couldn't.
/// </summary>
public sealed class UndoBarViewModelTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly InMemoryModLibrary _library = new();
    private readonly QueuedUiThread _uiThread = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly RecordingLogger<UndoBarViewModel> _logger = new();
    private readonly UndoBarViewModel _bar;

    public UndoBarViewModelTests()
    {
        _bar = new UndoBarViewModel(_library, _uiThread, _time, _logger);
    }

    public void Dispose() => _bar.Dispose();

    [Fact]
    public void ShowsNothingAtFirst()
    {
        Assert.Equal((false, false, string.Empty), (_bar.IsShown, _bar.CanUndo, _bar.Message));
        Assert.False(_bar.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void ShowsWhatHappenedForTenSeconds()
    {
        _bar.Offer("SkyUI pinned.", Pinned(SkyUi));

        Assert.Equal((true, true, "SkyUI pinned."), (_bar.IsShown, _bar.CanUndo, _bar.Message));
        Assert.True(_bar.UndoCommand.CanExecute(null));
        Assert.Equal(TimeSpan.FromSeconds(10), UndoBarViewModel.ShownFor);

        _time.Advance(TimeSpan.FromSeconds(9.9));
        Assert.True(_bar.IsShown);

        _time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal((false, false), (_bar.IsShown, _bar.CanUndo));
        Assert.False(_bar.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void ClosesOnTheUiThread()
    {
        _uiThread.Inline = false;
        _bar.Offer("SkyUI pinned.", Pinned(SkyUi));

        _time.Advance(UndoBarViewModel.ShownFor);
        Assert.True(_bar.IsShown);

        _uiThread.RunPending();
        Assert.False(_bar.IsShown);
    }

    [Fact]
    public void AnEndThatWaitedForTheUiThreadLeavesANewerChange()
    {
        _uiThread.Inline = false;
        _bar.Offer("SkyUI pinned.", Pinned(SkyUi));
        _time.Advance(UndoBarViewModel.ShownFor);

        _bar.Offer("SMAPI pinned.", Pinned(Smapi));
        _uiThread.RunPending();

        Assert.Equal((true, true, "SMAPI pinned."), (_bar.IsShown, _bar.CanUndo, _bar.Message));
    }

    [Fact]
    public async Task ANewerChangeTakesThePlaceOfTheOneOffered()
    {
        LibraryChange first = Pinned(SkyUi);
        _bar.Offer("SkyUI pinned.", first);
        _time.Advance(TimeSpan.FromSeconds(6));
        LibraryChange second = Pinned(Smapi);

        _bar.Offer("SMAPI pinned.", second);
        _time.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal((true, "SMAPI pinned."), (_bar.IsShown, _bar.Message));
        await _bar.UndoCommand.ExecuteAsync(null);
        Assert.Equal(["Restore 2400"], _library.Asked);
        Assert.False(Assert.Single(_library.Mods, mod => mod.Mod == Smapi).IsPinned);
        Assert.True(Assert.Single(_library.Mods, mod => mod.Mod == SkyUi).IsPinned);
    }

    [Fact]
    public async Task UndoingPutsTheModBackAndClosesTheBar()
    {
        LibraryChange pinned = Pinned(SkyUi);
        _bar.Offer("SkyUI pinned.", pinned);

        await _bar.UndoCommand.ExecuteAsync(null);

        Assert.Same(pinned.Before, Assert.Single(_library.Mods));
        Assert.Equal((false, false), (_bar.IsShown, _bar.CanUndo));

        await _bar.UndoCommand.ExecuteAsync(null);
        Assert.Equal(["Restore 12604"], _library.Asked);
    }

    [Fact]
    public void ClosingForgetsTheChange()
    {
        _bar.Offer("SkyUI pinned.", Pinned(SkyUi));

        _bar.CloseCommand.Execute(null);

        Assert.Equal((false, false), (_bar.IsShown, _bar.CanUndo));
        Assert.False(_bar.UndoCommand.CanExecute(null));
        Assert.Empty(_library.Asked);
    }

    [Fact]
    public async Task WhatCantBeUndoneSaysWhyForTenSeconds()
    {
        _bar.Offer("SkyUI removed from the library; its files stay in its folder.", Pinned(SkyUi));
        _library.RestoreRefusal = new InvalidOperationException("SkyUI is in the library again since, so it stays as it is now.");

        await _bar.UndoCommand.ExecuteAsync(null);

        Assert.Equal(
            (true, false, "Couldn't undo: SkyUI is in the library again since, so it stays as it is now."),
            (_bar.IsShown, _bar.CanUndo, _bar.Message));
        Assert.Empty(_logger.Entries);
        _time.Advance(UndoBarViewModel.ShownFor);
        Assert.False(_bar.IsShown);
    }

    [Fact]
    public async Task AnUndoThatFailsUnexpectedlyIsLoggedAndSaid()
    {
        _bar.Offer("SkyUI pinned.", Pinned(SkyUi));
        _library.RestoreRefusal = new IOException("The database is locked.");

        await _bar.UndoCommand.ExecuteAsync(null);

        Assert.Equal("Couldn't undo: Something went wrong. The log has the details.", _bar.Message);
        Assert.StartsWith(
            "Error: Couldn't undo a change to mod 12604 of skyrimspecialedition System.IO.IOException: The database is locked.",
            Assert.Single(_logger.Entries),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangeOfferedWhileUndoingStaysOffered()
    {
        _library.RestoreHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _bar.Offer("SkyUI pinned.", Pinned(SkyUi));
        Task undoing = _bar.UndoCommand.ExecuteAsync(null);

        _bar.Offer("SMAPI pinned.", Pinned(Smapi));
        _library.RestoreHeld.SetResult();
        await undoing;

        Assert.Equal((true, true, "SMAPI pinned."), (_bar.IsShown, _bar.CanUndo, _bar.Message));
    }

    /// <summary>
    /// The mod added to the library, then pinned.
    /// </summary>
    private LibraryChange Pinned(ModRef mod)
    {
        LibraryMod kept = TestData.LibraryMod(mod, mod.Id);
        _library.Add(kept);
        return _library.Change(mod, kept.WithPinned(true));
    }
}
