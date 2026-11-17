using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Selecting a game's mods in the Library, and Update selected, Watch, Unwatch and Remove: what's selected is always among
/// the mods shown.
/// </summary>
public sealed class LibrarySelectionTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("3863");
    private static readonly ModRef Other = TestData.Mod("1");
    private static readonly ModRef Smapi = TestData.Mod("2400", "stardewvalley");

    private readonly LibraryHarness _harness = new();

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task CountsTheSelectedModsAndTheirUpdates()
    {
        await StartAsync();
        (string, bool, bool?) before = (Screen.SelectionText, Screen.HasSelection, Screen.AreAllShownSelected);

        Row(SkyUi).IsSelected = true;
        Row(Other).IsSelected = true;
        (string, bool, string, bool, bool?) some = (Screen.SelectionText, Screen.HasSelection, Screen.UpdateSelectedText, Screen.UpdateSelectedCommand.CanExecute(null), Screen.AreAllShownSelected);
        Row(Address).IsSelected = true;
        bool? all = Screen.AreAllShownSelected;
        Screen.ClearSelectionCommand.Execute(null);

        Assert.Equal(("0 selected", false, false), before);
        Assert.Equal(("2 selected", true, "Update selected (2)", true, null), some);
        Assert.True(all);
        Assert.Equal(("0 selected", false, false), (Screen.SelectionText, Screen.HasSelection, Screen.AreAllShownSelected));
        Assert.All(Screen.GameMods, mod => Assert.False(mod.IsSelected));
    }

    [Fact]
    public async Task ModsWithoutUpdatesToDownloadCantBeUpdated()
    {
        await StartAsync();

        Row(Other).IsSelected = true;

        Assert.Equal(("Update selected (0)", false), (Screen.UpdateSelectedText, Screen.UpdateSelectedCommand.CanExecute(null)));
    }

    [Fact]
    public async Task SelectAllShownSelectsOnlyTheModsShown()
    {
        await StartAsync();
        _ = Row(SkyUi);
        Screen.ModsFilter = "lib";

        Screen.AreAllShownSelected = true;
        Screen.ModsFilter = string.Empty;
        string[] selected = [.. Screen.GameMods.Where(mod => mod.IsSelected).Select(mod => mod.Name)];
        Screen.AreAllShownSelected = false;

        Assert.Equal(["Address Library"], selected);
        Assert.Equal("0 selected", Screen.SelectionText);
    }

    [Fact]
    public async Task WhatAFilterHidesIsUnselected()
    {
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        Row(Address).IsSelected = true;
        LibraryModViewModel address = Row(Address);

        Screen.ModsFilter = "sky";
        (bool, string, bool?) filtered = (address.IsSelected, Screen.SelectionText, Screen.AreAllShownSelected);
        Screen.ModsFilter = string.Empty;

        Assert.Equal((false, "1 selected", true), filtered);
        Assert.False(address.IsSelected);
        Assert.Equal("1 selected", Screen.SelectionText);
    }

    [Fact]
    public async Task ATogglesHiddenModsAreUnselectedToo()
    {
        await StartAsync();
        LibraryModViewModel other = Row(Other);
        other.IsSelected = true;

        Screen.ModFilters.Single(toggle => toggle.Kind == ModFilterKind.HasUpdates).IsOn = true;

        Assert.False(other.IsSelected);
        Assert.False(Screen.HasSelection);
    }

    [Fact]
    public async Task AnotherGameOrTheGamesUnselectTheGamesMods()
    {
        await StartAsync();
        LibraryModViewModel skyUi = Row(SkyUi);
        skyUi.IsSelected = true;

        Row(Smapi).IsSelected = true;
        bool afterAnotherGame = skyUi.IsSelected;
        LibraryModViewModel smapi = Row(Smapi);
        Screen.ShowGamesCommand.Execute(null);

        Assert.False(afterAnotherGame);
        Assert.False(smapi.IsSelected);
        Assert.False(Screen.HasSelection);
    }

    [Fact]
    public async Task UpdateSelectedDownloadsTheSelectedModsUpdatesThenUnselectsThem()
    {
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        Row(Address).IsSelected = true;
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);
        List<(bool, bool)> during = [];
        _harness.LibraryDownloads.Answer = _ =>
        {
            during.Add((Screen.UpdateSelectedCommand.CanExecute(null), skyrim.UpdateAllCommand.CanExecute(null)));
            return Queued();
        };

        await Screen.UpdateSelectedCommand.ExecuteAsync(null);

        Assert.Equal(
            ["3863?tab=files&file_id=2", "12604?tab=files&file_id=3", "12604?tab=files&file_id=2"],
            _harness.LibraryDownloads.Added.Select(link => link.Segments[^1] + link.Query));
        Assert.All(during, state => Assert.Equal((false, false), state));
        Assert.Equal("Queued 3 updates. They show on the Downloads screen.", skyrim.UpdateNote);
        Assert.False(Screen.HasSelection);
        Assert.True(skyrim.UpdateAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhileAGamesUpdatesDownloadUpdateSelectedWaits()
    {
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        LibraryGameViewModel skyrim = _harness.Game(SkyUi.Game);
        bool? during = null;
        _harness.LibraryDownloads.Answer = _ =>
        {
            during = Screen.UpdateSelectedCommand.CanExecute(null);
            return Queued();
        };

        await skyrim.UpdateAllCommand.ExecuteAsync(null);

        Assert.False(during);
        Assert.True(Screen.UpdateSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task AModThatLeavesTheLibraryLeavesTheSelection()
    {
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        Row(Other).IsSelected = true;

        _harness.Library.Change(SkyUi, null);

        Assert.Equal(("1 selected", "Update selected (0)"), (Screen.SelectionText, Screen.UpdateSelectedText));
    }

    [Fact]
    public async Task SaysWhatWatchUnwatchAndRemoveWouldDoToTheSelection()
    {
        await StartAsync();
        await Row(Other).UnwatchCommand.ExecuteAsync(null);

        Row(SkyUi).IsSelected = true;
        Row(Address).IsSelected = true;
        (string, bool, string, bool, string, bool) watchedOnly = Buttons();
        Row(Other).IsSelected = true;

        Assert.Equal(("Watch (0)", false, "Unwatch (2)", true, "Remove (2)", true), watchedOnly);
        Assert.Equal(("Watch (1)", true, "Unwatch (2)", true, "Remove (3)", true), Buttons());
    }

    [Fact]
    public async Task UnwatchSelectedUnwatchesThemOfferingToUndoThemTogether()
    {
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        Row(Address).IsSelected = true;

        await Screen.UnwatchSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["Unwatch 3863", "Unwatch 12604"], _harness.Library.Asked);
        Assert.Equal((true, true, "2 mods are no longer watched."), (_harness.Undo.IsShown, _harness.Undo.CanUndo, _harness.Undo.Message));
        Assert.Equal(("0 selected", false), (Screen.SelectionText, Screen.HasSelection));
        Assert.Equal([false, true, false], Screen.GameMods.OrderBy(mod => mod.Name, StringComparer.Ordinal).Select(mod => mod.IsWatched));

        await _harness.Undo.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["Unwatch 3863", "Unwatch 12604", "Restore 12604", "Restore 3863"], _harness.Library.Asked);
        Assert.All(Screen.GameMods, mod => Assert.True(mod.IsWatched));
    }

    [Fact]
    public async Task WatchSelectedWatchesOnlyThoseNotWatchedAndOffersNoUndo()
    {
        await StartAsync();
        await Row(Other).UnwatchCommand.ExecuteAsync(null);
        _harness.Undo.CloseCommand.Execute(null);
        Row(SkyUi).IsSelected = true;
        Row(Other).IsSelected = true;

        await Screen.WatchSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["Unwatch 1", "Watch 1"], _harness.Library.Asked);
        Assert.True(Row(Other).IsWatched);
        Assert.False(_harness.Undo.IsShown);
        Assert.Equal(("1 selected", true), (Screen.SelectionText, Row(SkyUi).IsSelected));
    }

    [Fact]
    public async Task RemoveSelectedRemovesThemOfferingToBringThemAllBack()
    {
        await StartAsync();
        _ = Row(SkyUi);
        Screen.AreAllShownSelected = true;

        await Screen.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["Forget 3863", "Forget 1", "Forget 12604"], _harness.Library.Asked);
        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Equal("3 mods removed from the library; their files stay in their folders.", _harness.Undo.Message);
        Assert.DoesNotContain(_harness.Library.Mods, mod => mod.Mod.Game == SkyUi.Game);

        await _harness.Undo.UndoCommand.ExecuteAsync(null);

        Assert.Equal(["Restore 12604", "Restore 1", "Restore 3863"], _harness.Library.Asked.Skip(3));
        Assert.Equal(4, _harness.Library.Mods.Count);
    }

    [Fact]
    public async Task OneModRemovedIsNamed()
    {
        await StartAsync();
        Row(SkyUi).IsSelected = true;

        await Screen.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.Equal("SkyUI removed from the library; its files stay in its folder.", _harness.Undo.Message);
    }

    [Fact]
    public async Task WhatCouldntBeDoneIsSaidOnceAndStaysSelected()
    {
        await StartAsync();
        Row(SkyUi).IsSelected = true;
        Row(Address).IsSelected = true;
        _harness.Library.Refusal = new InvalidOperationException("A download of one of its files isn't finished.");

        await Screen.RemoveSelectedCommand.ExecuteAsync(null);

        (string Title, string Message) said = Assert.Single(_harness.Dialogs.Messages);
        Assert.Equal("Some mods weren't removed", said.Title);
        Assert.Equal(
            ["Address Library: A download of one of its files isn't finished.", "SkyUI: A download of one of its files isn't finished."],
            said.Message.Split(Environment.NewLine));
        Assert.Equal("2 selected", Screen.SelectionText);
        Assert.False(_harness.Undo.IsShown);
        Assert.True(Screen.RemoveSelectedCommand.CanExecute(null));
    }

    private (string, bool, string, bool, string, bool) Buttons()
        => (Screen.WatchSelectedText, Screen.WatchSelectedCommand.CanExecute(null),
            Screen.UnwatchSelectedText, Screen.UnwatchSelectedCommand.CanExecute(null),
            Screen.RemoveSelectedText, Screen.RemoveSelectedCommand.CanExecute(null));

    private LibraryModViewModel Row(ModRef mod) => _harness.Row(TestData.LibraryMod(mod, "any"));

    private Task StartAsync()
        => _harness.StartAsync(
            WithUpdates(SkyUi, "SkyUI", Update(SkyUi, "2", daysAgo: 2), Update(SkyUi, "3", daysAgo: 1)),
            WithUpdates(Address, "Address Library", Update(Address, "2", daysAgo: 0)),
            TestData.LibraryMod(Other, "Other", watched: true, TestData.LibraryFile(Other, "1", "Other.7z")),
            WithUpdates(Smapi, "SMAPI", Update(Smapi, "2", daysAgo: 0)));

    private static LibraryMod WithUpdates(ModRef mod, string name, params LibraryUpdate[] updates)
        => TestData.LibraryMod(mod, name, watched: true, TestData.LibraryFile(mod, "1", $"{name}.7z")).WithUpdates(updates);

    private static LibraryUpdate Update(ModRef mod, string id, int daysAgo)
        => new(
            new ModFileRef(mod, id),
            $"Update {id}",
            $"update-{id}.7z",
            id,
            ModFileCategory.Main,
            TestData.Now.AddDays(-daysAgo),
            new ModFileRef(mod, "1"),
            TestData.Now,
            new Uri($"https://www.nexusmods.com/{mod.Game.Key}/mods/{mod.Id}?tab=files&file_id={id}"));

    private static LibraryDownload Queued()
    {
        string folder = Path.Combine(TestData.LibraryRoot, "mods");
        return new LibraryDownload(Path.Combine(folder, "file.7z"), new LinkDownload(TestData.Job("file.7z", folder: folder), AlreadyQueued: false));
    }
}
