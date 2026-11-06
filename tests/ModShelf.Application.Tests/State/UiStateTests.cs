using ModShelf.Application.State;

namespace ModShelf.Application.Tests.State;

/// <summary>
/// What the desktop app remembers: Browse's recent searches, the newest first, each once; the screen by its name; and
/// where the window was.
/// </summary>
public sealed class UiStateTests
{
    [Fact]
    public void RemembersNothingAtFirst()
    {
        Assert.Equal((null, null, null), (UiState.None.Window, UiState.None.MenuCollapsed, UiState.None.Screen));
        Assert.Empty(UiState.None.BrowseSearches);
    }

    [Fact]
    public void TheNewestSearchComesFirst()
    {
        UiState state = UiState.None.WithBrowseSearch("skyui").WithBrowseSearch("armour").WithBrowseSearch("  dragons  ");

        Assert.Equal(["dragons", "armour", "skyui"], state.BrowseSearches);
    }

    [Fact]
    public void ASearchMadeAgainMovesUpInItsNewCase()
    {
        UiState state = UiState.None.WithBrowseSearch("skyui").WithBrowseSearch("armour").WithBrowseSearch("SkyUI");

        Assert.Equal(["SkyUI", "armour"], state.BrowseSearches);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptySearchChangesNothing(string? text)
    {
        UiState state = UiState.None.WithBrowseSearch("skyui");

        Assert.Same(state, state.WithBrowseSearch(text));
    }

    [Fact]
    public void KeepsTheNewestTenSearches()
    {
        UiState state = UiState.None;
        for (int search = 1; search <= 12; search++)
        {
            state = state.WithBrowseSearch($"search {search}");
        }

        Assert.Equal(UiState.MaxBrowseSearches, state.BrowseSearches.Count);
        Assert.Equal(("search 12", "search 3"), (state.BrowseSearches[0], state.BrowseSearches[^1]));
    }

    [Fact]
    public void SearchesGivenAtOnceAreKeptAsTheyWouldBeOneByOne()
    {
        UiState state = new() { BrowseSearches = ["new", " ", "Old", "old", .. Enumerable.Range(1, 20).Select(search => $"{search}")] };

        Assert.Equal(["new", "Old", "1", "2", "3", "4", "5", "6", "7", "8"], state.BrowseSearches);
        Assert.Throws<ArgumentNullException>(() => new UiState { BrowseSearches = null! });
    }

    [Theory]
    [InlineData(" browse ", "browse")]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void TheScreenIsNamedTrimmed(string? screen, string? kept)
    {
        Assert.Equal(kept, new UiState { Screen = screen }.Screen);
    }

    [Fact]
    public void QuickSearchesKeepTheNewestTenFirstApartFromBrowses()
    {
        UiState state = UiState.None.WithBrowseSearch("skyui");
        foreach (int search in Enumerable.Range(1, 12))
        {
            state = state.WithQuickSearch($" search {search} ");
        }

        state = state.WithQuickSearch("SEARCH 5").WithQuickSearch("  ").WithQuickSearch(null);

        Assert.Equal(["SEARCH 5", "search 12", "search 11", "search 10", "search 9", "search 8", "search 7", "search 6", "search 4", "search 3"], state.QuickSearches);
        Assert.Equal(["skyui"], state.BrowseSearches);
        Assert.Equal(10, UiState.MaxQuickSearches);
    }

    [Fact]
    public void StatesThatRememberTheSameAreEqual()
    {
        UiState state = new()
        {
            Window = new WindowPlacement(10, -20, 1280, 800, maximized: true),
            MenuCollapsed = true,
            Screen = "library",
            BrowseSearches = ["skyui", "armour"],
            QuickSearches = ["dragon", "old versions"],
        };
        UiState same = new()
        {
            Window = new WindowPlacement(10, -20, 1280, 800, maximized: true),
            MenuCollapsed = true,
            Screen = " library",
            BrowseSearches = ["skyui", "armour"],
            QuickSearches = ["dragon", "old versions"],
        };

        Assert.Equal(state, same);
        Assert.Equal(state.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(state, same with { BrowseSearches = ["armour", "skyui"] });
        Assert.NotEqual(state, same with { MenuCollapsed = false });
        Assert.NotEqual(state, same with { Window = new WindowPlacement(10, -20, 1280, 800, maximized: false) });
        Assert.NotEqual(state, same with { Screen = "browse" });
        Assert.NotEqual(state, same with { QuickSearches = ["dragon"] });
        Assert.NotEqual(state, same with { QuickSearches = ["old versions", "dragon"] });
        Assert.False(state.Equals(null));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(-1, 600)]
    [InlineData(800, 0)]
    [InlineData(double.NaN, 600)]
    [InlineData(800, double.PositiveInfinity)]
    public void AWindowHasASize(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowPlacement(0, 0, width, height, maximized: false));
    }

    [Fact]
    public void AWindowKeepsWhereItWas()
    {
        WindowPlacement window = new(-1920, 40, 1024.5, 700, maximized: true);

        Assert.Equal((-1920, 40, 1024.5, 700d, true), (window.X, window.Y, window.Width, window.Height, window.Maximized));
    }
}
