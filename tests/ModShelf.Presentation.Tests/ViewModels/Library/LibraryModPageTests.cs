using ModShelf.Application.Catalog;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Tests.ViewModels.Browse;
using ModShelf.Presentation.ViewModels.Library;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Tests.ViewModels.Library;

/// <summary>
/// A library mod's page, opened in place of the list: from its site, or from its saved details when the site can't say.
/// </summary>
public sealed class LibraryModPageTests : IDisposable
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");
    private static readonly ModRef Address = TestData.Mod("32444");
    private static readonly LibraryMod InLibrary = TestData.LibraryMod(SkyUi, "SkyUI", files: [TestData.LibraryFile(SkyUi, "1", "SkyUI.7z")]);

    private readonly LibraryHarness _harness = new();

    private LibraryViewModel Screen => _harness.Screen;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OpensAModFromItsRowInPlaceOfTheList()
    {
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI from the site"), BrowseHarness.File(SkyUi, "1"));
        await _harness.StartAsync(InLibrary);

        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);
        await Screen.Opening;

        ModPageViewModel mod = Assert.IsType<ModPageViewModel>(Screen.Mod);
        Assert.Equal("SkyUI from the site", mod.Name);
        Assert.True(Screen.IsModOpen);
        Assert.False(Screen.IsOpening);
        Assert.Equal("Skyrim Special Edition", Screen.OpenGameName);
        Assert.Null(Screen.PageProblem);
        Assert.False(Screen.RetryCommand.CanExecute(null));
        Assert.True(mod.IsInLibrary);
        Assert.True(Assert.Single(mod.Files).IsInLibrary);
        Assert.Equal(["SkyUI.7z"], mod.OwnFiles.Select(file => file.FileName));
    }

    [Fact]
    public async Task GoesBackToTheList()
    {
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"));
        await _harness.StartAsync(InLibrary);
        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);

        Screen.BackCommand.Execute(null);

        Assert.Null(Screen.Mod);
        Assert.False(Screen.IsModOpen);
        Assert.False(Screen.BackCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShowsTheSavedDetailsWhenTheSiteCantSayThenTriesAgain()
    {
        _harness.Lookup.Refusals["LookUpAsync"] = new HttpRequestException("No connection.");
        _harness.Pictures.Details.Saved[SkyUi] = new ModInfo(SkyUi, "SkyUI as saved", summary: "Saved.", pageUrl: TestData.ModPage);
        await _harness.StartAsync(InLibrary);

        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);
        ModPageViewModel offline = Assert.IsType<ModPageViewModel>(Screen.Mod);
        string? problem = Screen.PageProblem;
        bool couldRetry = Screen.RetryCommand.CanExecute(null);

        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"), BrowseHarness.File(SkyUi, "1"));
        await Screen.RetryCommand.ExecuteAsync(null);

        Assert.Equal("SkyUI as saved", offline.Name);
        Assert.Empty(offline.Files);
        Assert.Equal((true, false, false), (offline.IsOffline, offline.ShowsNoFiles, offline.ShowsNoChangelog));
        Assert.Equal("Shown once Nexus Mods gives the mod.", offline.OfflineText);
        Assert.Equal(["SkyUI.7z"], offline.OwnFiles.Select(file => file.FileName));
        Assert.Equal("Nexus Mods couldn't give the mod, so its saved details show: No connection.", problem);
        Assert.True(couldRetry);
        Assert.Equal("SkyUI", Screen.Mod?.Name);
        Assert.Null(Screen.PageProblem);
        Assert.Single(Screen.Mod!.Files);
        Assert.False(Screen.Mod.IsOffline);
    }

    [Fact]
    public async Task ShowsWhatTheLibraryKnowsWithoutSavedDetails()
    {
        _harness.Lookup.Refusals["LookUpAsync"] = new HttpRequestException("No connection.");
        await _harness.StartAsync(InLibrary);

        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);

        ModPageViewModel mod = Assert.IsType<ModPageViewModel>(Screen.Mod);
        Assert.Equal(("SkyUI", "by An Author"), (mod.Name, mod.AuthorText));
        Assert.Equal("Skyrim Special Edition", Screen.OpenGameName);
        Assert.NotNull(Screen.PageProblem);
    }

    [Fact]
    public async Task SaysSoWhenOpeningFailsForAnotherReason()
    {
        _harness.Lookup.Refusals["LookUpAsync"] = new InvalidOperationException("A bug.");
        await _harness.StartAsync(InLibrary);

        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);

        Assert.Null(Screen.Mod);
        Assert.False(Screen.IsOpening);
        Assert.Equal(Strings.ActionFailedTitle, Assert.Single(_harness.Dialogs.Messages).Title);
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: Opening mod 12604 of skyrimspecialedition failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpensAModTheOpenModNeeds()
    {
        LibraryMod address = TestData.LibraryMod(Address, "Address Library");
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI")) with { Requirements = [new ModRequirement("Address Library", Address)] };
        _harness.Lookup.Pages[Address] = BrowseHarness.Page(BrowseHarness.Info(Address, "Address Library"));
        await _harness.StartAsync(InLibrary, address);
        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);
        RequirementViewModel needed = Assert.Single(Screen.Mod!.Requirements);

        await needed.OpenCommand.ExecuteAsync(null);

        Assert.Equal(Address, Screen.Mod?.Mod);
        Assert.False(needed.IsInLibrary);
    }

    [Fact]
    public async Task ShowsTheLibrarysChangesOnTheOpenMod()
    {
        _harness.Lookup.Pages[SkyUi] = BrowseHarness.Page(BrowseHarness.Info(SkyUi, "SkyUI"), BrowseHarness.File(SkyUi, "1"), BrowseHarness.File(SkyUi, "2"))
            with { Requirements = [new ModRequirement("Address Library", Address)] };
        await _harness.StartAsync(InLibrary);
        await _harness.Row(InLibrary).OpenCommand.ExecuteAsync(null);
        ModPageViewModel mod = Screen.Mod!;

        _harness.Library.Change(SkyUi, InLibrary.WithFile(TestData.LibraryFile(SkyUi, "2", "SkyUI 2.7z", daysAgo: 0)));
        _harness.Library.Change(Address, TestData.LibraryMod(Address, "Address Library", files: [TestData.LibraryFile(Address, "1", "Address.7z")]));
        IReadOnlyList<string> files = [.. mod.OwnFiles.Select(file => file.FileName)];
        bool[] onSite = [.. mod.Files.OrderBy(file => file.File.Id, StringComparer.Ordinal).Select(file => file.IsInLibrary)];
        _harness.Library.Change(SkyUi, null);

        Assert.Equal(["SkyUI 2.7z", "SkyUI.7z"], files);
        Assert.Equal([true, true], onSite);
        Assert.True(mod.Requirements[0].IsInLibrary);
        Assert.False(mod.IsInLibrary);
        Assert.Empty(mod.OwnFiles);
    }
}
