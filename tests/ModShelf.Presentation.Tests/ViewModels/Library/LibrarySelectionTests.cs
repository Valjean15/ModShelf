using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.ViewModels.Library;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// Selecting a game's mods in the Library, and Update selected: what's selected is always among the mods shown.
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
