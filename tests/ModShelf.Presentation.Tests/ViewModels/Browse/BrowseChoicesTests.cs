using ModShelf.Presentation.ViewModels.Browse;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.Tests.ViewModels.Browse;

/// <summary>
/// What Browse offers for a site, from what its search can do and its words.
/// </summary>
public sealed class BrowseChoicesTests
{
    private static readonly SiteWords Likes = new() { Approval = "like", Approvals = "likes" };

    [Fact]
    public void EveryChoiceForASiteWhoseSearchIsntKnown()
    {
        BrowseOffer every = BrowseChoices.For(capabilities: null, SiteWords.None);

        Assert.Equal(14, every.Sorts.Count);
        Assert.Same(every.Sorts[0], every.Default);
        Assert.Equal((4, 3), (every.TimeWindows.Count, every.PageSizes.Count));
        Assert.True(every.Has(SearchFilters.Text | SearchFilters.Author | SearchFilters.Category | SearchFilters.UpdatedSince | SearchFilters.AdultContent));
    }

    [Fact]
    public void OnlyTheSitesOrdersInBrowsesOrderNamedInItsWords()
    {
        BrowseOffer offer = BrowseChoices.For(
            new SearchCapabilities([new(SearchSort.Newest, SearchDirection.Descending), .. SearchOrder.BothWays(SearchSort.Endorsements)], SearchFilters.None, 100),
            Likes);

        Assert.Equal(["Most likes", "Fewest likes", "Newest"], offer.Sorts.Select(sort => sort.Name));
        Assert.Equal("Newest", offer.Default.Name);
    }

    [Theory]
    [InlineData(100, new[] { 20, 50, 100 })]
    [InlineData(50, new[] { 20, 50 })]
    [InlineData(49, new[] { 20 })]
    [InlineData(10, new[] { 10 })]
    public void PageSizesUpToTheSitesLargest(int maxCount, int[] sizes)
    {
        BrowseOffer offer = BrowseChoices.For(new SearchCapabilities([new(SearchSort.Relevance, SearchDirection.Descending)], SearchFilters.None, maxCount), SiteWords.None);

        Assert.Equal(sizes, offer.PageSizes.Select(size => size.Count));
        Assert.Equal($"{sizes[^1]} per page", offer.PageSizes[^1].Name);
    }

    [Fact]
    public void UpdateWindowsOnlyWhereTheSiteSearchesByThem()
    {
        SearchOrder best = new(SearchSort.Relevance, SearchDirection.Descending);

        Assert.Equal([null], BrowseChoices.For(new SearchCapabilities([best], SearchFilters.Text, 20), SiteWords.None).TimeWindows.Select(window => window.Within));
        Assert.Equal(4, BrowseChoices.For(new SearchCapabilities([best], SearchFilters.UpdatedSince, 20), SiteWords.None).TimeWindows.Count);
    }

    [Fact]
    public void EveryOrderWhenBrowseListsNoneOfTheSites()
    {
        BrowseOffer offer = BrowseChoices.For(new SearchCapabilities([new(SearchSort.Relevance, SearchDirection.Ascending)], SearchFilters.None, 20), SiteWords.None);

        Assert.Equal(14, offer.Sorts.Count);
        Assert.Same(offer.Sorts[0], offer.Default);
    }

    [Fact]
    public void ApprovalWithoutTheSitesWordIsCalledApprovals()
    {
        BrowseOffer offer = BrowseChoices.For(capabilities: null, SiteWords.None);

        Assert.Equal(["Most approvals", "Fewest approvals"], offer.Sorts.Where(sort => sort.Sort == SearchSort.Endorsements).Select(sort => sort.Name));
    }
}
