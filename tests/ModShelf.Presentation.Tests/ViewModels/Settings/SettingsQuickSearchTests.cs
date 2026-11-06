using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// The Settings screen for quick search: each part listed by its category and heading, and a category opened, ending a
/// search.
/// </summary>
public sealed class SettingsQuickSearchTests
{
    private readonly SettingsHarness _harness = new();

    private SettingsViewModel Screen => _harness.Screen;

    [Fact]
    public void ListsEachPartByItsCategoryAndHeading()
    {
        IReadOnlyList<SettingsSearchEntry> entries = ((ISettingsIndex)Screen).Entries;

        Assert.Contains(entries, entry => entry is { Category: SettingsCategory.General, CategoryName: "General", Heading: "Appearance" });
        Assert.All(entries, entry => Assert.Equal(Screen.Categories.Single(category => category.Category == entry.Category).Name, entry.CategoryName));
        Assert.All(entries, entry => Assert.StartsWith(entry.Heading, entry.Words, StringComparison.Ordinal));
        Assert.Equal(entries.Count, entries.Select(entry => (entry.Category, entry.Heading)).Distinct().Count());
        Assert.Equal(Enum.GetValues<SettingsCategory>(), entries.Select(entry => entry.Category).Distinct().Order());
    }

    [Fact]
    public void OpensACategoryEndingTheSearch()
    {
        Screen.SearchText = "folder";

        _harness.Screens.OpenSettings(SettingsCategory.Updates);

        Assert.Equal((string.Empty, SettingsCategory.Updates, Screen.Screen), (Screen.SearchText, Screen.SelectedCategory?.Category, _harness.Navigation.Current));
        Assert.False(Screen.IsSearching);
    }

    [Fact]
    public void OpeningTheCategoryShownEndsTheSearchToo()
    {
        Screen.SearchText = "folder";

        _harness.Screens.OpenSettings(SettingsCategory.General);

        Assert.Equal((string.Empty, SettingsCategory.General), (Screen.SearchText, Screen.SelectedCategory?.Category));
    }
}
