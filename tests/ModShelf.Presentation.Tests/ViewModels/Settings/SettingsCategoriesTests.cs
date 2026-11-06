using ModShelf.Application.Library;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The Settings screen's categories, one shown at a time; the search, which shows what matches from every category;
/// and the Library category's rescan.
/// </summary>
public sealed class SettingsCategoriesTests
{
    private readonly SettingsHarness _harness = new();

    private SettingsViewModel Screen => _harness.Screen;

    [Fact]
    public void ShowsGeneralFirstThenEachCategorysSettingsAlone()
    {
        Dictionary<string, string[]> shown = [];

        foreach (SettingsCategoryViewModel category in Screen.Categories)
        {
            Screen.SelectedCategory = category;
            shown[category.Name] = Shown();
        }

        Assert.Equal(["General", "Library", "Updates", "Links", "Agents", "Games", "About"], Screen.Categories.Select(category => category.ToString()));
        Assert.Equal(["Theme", "Window", "Pictures"], shown["General"]);
        Assert.Equal(["LibraryFolder", "FolderPattern", "OldVersions", "Rescan"], shown["Library"]);
        Assert.Equal(["Updates", "Quota"], shown["Updates"]);
        Assert.Equal(["Links"], shown["Links"]);
        Assert.Equal(["Agents"], shown["Agents"]);
        Assert.Equal(["Games"], shown["Games"]);
        Assert.Equal(["About"], shown["About"]);
        Assert.All(Screen.Categories, category => Assert.True(category.HasMatches));
    }

    [Fact]
    public void StartsOnGeneral()
    {
        Assert.Equal(SettingsCategory.General, Screen.SelectedCategory?.Category);
        Assert.Equal(["Theme", "Window", "Pictures"], Shown());
        Assert.Equal((false, string.Empty, (string?)null), (Screen.IsSearching, Screen.SearchHeading, Screen.NothingMatches));
    }

    [Fact]
    public void ASearchShowsWhatMatchesFromEveryCategory()
    {
        Screen.SelectedCategory = Screen.Categories[5];

        Screen.SearchText = "  FOLDER ";

        Assert.True(Screen.IsSearching);
        Assert.Equal("Settings that match “FOLDER”", Screen.SearchHeading);
        // The archive folder, the mods' folders, the log folder.
        Assert.Equal(["LibraryFolder", "FolderPattern", "OldVersions", "Rescan", "Games", "About"], Shown());
        Assert.Equal([false, true, false, false, false, true, true], Screen.Categories.Select(category => category.HasMatches));
        Assert.Null(Screen.NothingMatches);
    }

    [Fact]
    public void EveryWordSearchedForMustMatchInAnyCaseAndWithOrWithoutAccents()
    {
        Screen.SearchText = "notice updatés";

        // Every game's notices, and a game's own.
        Assert.Equal(["Updates", "Games"], Shown());
    }

    [Fact]
    public void ASearchFindsASectionsHeading()
    {
        Screen.SearchText = "appearance";

        Assert.Equal(["Theme"], Shown());
    }

    [Fact]
    public void ASearchThatFindsNothingSaysSo()
    {
        Screen.SearchText = "zzz";

        Assert.Empty(Shown());
        Assert.Equal("No setting matches “zzz”.", Screen.NothingMatches);
        Assert.All(Screen.Categories, category => Assert.False(category.HasMatches));
    }

    [Fact]
    public void ClearingTheSearchShowsTheCategoryChosenAgain()
    {
        Screen.SelectedCategory = Screen.Categories[1];
        Screen.SearchText = "theme";

        Screen.ClearSearchCommand.Execute(null);

        Assert.Equal((string.Empty, false), (Screen.SearchText, Screen.IsSearching));
        Assert.Equal(["LibraryFolder", "FolderPattern", "OldVersions", "Rescan"], Shown());
        Assert.All(Screen.Categories, category => Assert.True(category.HasMatches));
    }

    [Fact]
    public void ClearingASearchThatFoundNothingSaysNoMore()
    {
        Screen.SearchText = "zzz";

        Screen.ClearSearchCommand.Execute(null);

        Assert.Equal((string.Empty, (string?)null), (Screen.SearchHeading, Screen.NothingMatches));
        Assert.Equal(["Theme", "Window", "Pictures"], Shown());
    }

    [Fact]
    public void ChoosingACategoryEndsTheSearch()
    {
        Screen.SearchText = "theme";

        Screen.SelectedCategory = Screen.Categories[2];

        Assert.Equal(string.Empty, Screen.SearchText);
        Assert.Equal(["Updates", "Quota"], Shown());
    }

    [Fact]
    public void BlankSearchesSearchNothing()
    {
        Screen.SelectedCategory = Screen.Categories[6];

        Screen.SearchText = " \t ";

        Assert.False(Screen.IsSearching);
        Assert.Equal(["About"], Shown());
    }

    [Fact]
    public async Task RescansTheWholeLibraryAndSaysWhatItFound()
    {
        await _harness.StartAsync();

        await Screen.RescanCommand.ExecuteAsync(null);

        Assert.Equal([null], _harness.Rescan.Asked);
        Assert.Equal("Rescan finished", Assert.Single(_harness.Dialogs.Messages).Title);
    }

    [Fact]
    public async Task ARescanThatFailsUnexpectedlyIsLoggedAndSaid()
    {
        await _harness.StartAsync();
        _harness.Rescan.Refusal = new InvalidOperationException("Something broke.");

        await Screen.RescanCommand.ExecuteAsync(null);

        Assert.StartsWith(
            "Error: Rescanning the library from the Settings screen failed System.InvalidOperationException",
            Assert.Single(_harness.Logger.Entries),
            StringComparison.Ordinal);
        Assert.Single(_harness.Dialogs.Messages);
    }

    /// <summary>
    /// The sections shown, in the screen's order, by their names without "Section".
    /// </summary>
    private string[] Shown()
        => [.. new (string Name, SettingsSectionViewModel Section)[]
        {
            ("Theme", Screen.ThemeSection),
            ("Window", Screen.WindowSection),
            ("Pictures", Screen.PicturesSection),
            ("LibraryFolder", Screen.LibraryFolderSection),
            ("FolderPattern", Screen.FolderPatternSection),
            ("OldVersions", Screen.OldVersionsSection),
            ("Rescan", Screen.RescanSection),
            ("Updates", Screen.UpdatesSection),
            ("Quota", Screen.QuotaSection),
            ("Links", Screen.LinksSection),
            ("Agents", Screen.AgentsSection),
            ("Games", Screen.GamesSection),
            ("About", Screen.AboutSection),
        }.Where(section => section.Section.IsShown).Select(section => section.Name)];
}
