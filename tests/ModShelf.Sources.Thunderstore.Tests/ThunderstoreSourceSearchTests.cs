using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Thunderstore.Tests.Common;

using static ModShelf.Sources.Thunderstore.Tests.Common.ThunderstoreHarness;

namespace ModShelf.Sources.Thunderstore.Tests;

/// <summary>
/// A community's packages searched from its listing: deprecated ones left out, as Thunderstore's website does, and adult
/// ones unless asked.
/// </summary>
public sealed class ThunderstoreSourceSearchTests : IDisposable
{
    private static readonly DateTimeOffset January = TestPackage.January;

    private readonly ThunderstoreHarness _harness = new();

    public ThunderstoreSourceSearchTests()
    {
        _harness.List(
            new TestPackage("Jowday", "DamageMeter") { Updated = January.AddDays(5), Created = January.AddDays(-10), Likes = 30, Categories = ["Tools", "Mods"], Description = "Shows the damage dealt." },
            new TestPackage("zerp", "Weapon_Fusion") { Updated = January.AddDays(4), Created = January.AddDays(-5), Likes = 10, Description = "Fuses weapons.", Versions = [new("2.0.0", January, 9000, 500)] },
            new TestPackage("zerp", "Mel_Skin") { Updated = January.AddDays(3), Created = January.AddDays(-20), Likes = 20, Categories = ["Cosmetics"], Adult = true },
            new TestPackage("Old", "Deprecated") { Updated = January.AddDays(6), Deprecated = true },
            new TestPackage("Pony", "Menu") { Updated = January.AddDays(1), Created = January.AddDays(-1), Likes = 0, Description = "A menu of damage options." });
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ListsByWhenUpdatedFirstLeavingOutDeprecatedAndAdultPackages()
    {
        SearchPage page = await SearchAsync(new SearchQuery());

        Assert.Equal(["Jowday-DamageMeter", "zerp-Weapon_Fusion", "Pony-Menu"], Ids(page));
        Assert.Equal((3, false), (page.TotalCount, page.HasMore));
    }

    [Fact]
    public async Task IncludesAdultPackagesWhenAsked()
    {
        SearchPage page = await SearchAsync(new SearchQuery(includeAdultContent: true));

        Assert.Contains("zerp-Mel_Skin", Ids(page));
        Assert.True(Assert.Single(page.Mods, mod => mod.Mod.Id == "zerp-Mel_Skin").HasAdultContent);
    }

    [Theory]
    [InlineData("damage", new[] { "Jowday-DamageMeter", "Pony-Menu" })]
    [InlineData("weapon fusion", new[] { "zerp-Weapon_Fusion" })]
    [InlineData("ZERP", new[] { "zerp-Weapon_Fusion" })]
    [InlineData("nothing like it", new string[0])]
    public async Task FindsWordsInTheNameTeamOrDescription(string words, string[] found)
    {
        Assert.Equal(found, Ids(await SearchAsync(new SearchQuery(words))));
    }

    [Fact]
    public async Task NarrowsByTeamCategoryAndUpdate()
    {
        Assert.Equal(["zerp-Weapon_Fusion", "zerp-Mel_Skin"], Ids(await SearchAsync(new SearchQuery(author: "Zerp", includeAdultContent: true))));
        Assert.Equal(["Pony-Menu"], Ids(await SearchAsync(new SearchQuery(updatedSince: January.AddDays(1), sort: SearchSort.Updated, direction: SearchDirection.Ascending, count: 1))));
        Assert.Equal(["Jowday-DamageMeter", "zerp-Weapon_Fusion"], Ids(await SearchAsync(new SearchQuery(updatedSince: January.AddDays(4)))));

        // Found by a category it has after its first, it's described by that one.
        SearchPage mods = await SearchAsync(new SearchQuery(category: "mods"));
        Assert.Equal([("Jowday-DamageMeter", "Mods"), ("zerp-Weapon_Fusion", "Mods"), ("Pony-Menu", "Mods")], mods.Mods.Select(mod => (mod.Mod.Id, mod.Category)));
        Assert.Equal("Tools", (await SearchAsync(new SearchQuery("meter"))).Mods[0].Category);
    }

    [Theory]
    [InlineData(SearchSort.Newest, SearchDirection.Descending, new[] { "Pony-Menu", "zerp-Weapon_Fusion", "Jowday-DamageMeter" })]
    [InlineData(SearchSort.Endorsements, SearchDirection.Descending, new[] { "Jowday-DamageMeter", "zerp-Weapon_Fusion", "Pony-Menu" })]
    [InlineData(SearchSort.Endorsements, SearchDirection.Ascending, new[] { "Pony-Menu", "zerp-Weapon_Fusion", "Jowday-DamageMeter" })]
    [InlineData(SearchSort.Downloads, SearchDirection.Descending, new[] { "zerp-Weapon_Fusion", "Jowday-DamageMeter", "Pony-Menu" })]
    [InlineData(SearchSort.Size, SearchDirection.Descending, new[] { "zerp-Weapon_Fusion", "Jowday-DamageMeter", "Pony-Menu" })]
    [InlineData(SearchSort.Name, SearchDirection.Ascending, new[] { "Jowday-DamageMeter", "Pony-Menu", "zerp-Weapon_Fusion" })]
    [InlineData(SearchSort.Name, SearchDirection.Descending, new[] { "zerp-Weapon_Fusion", "Pony-Menu", "Jowday-DamageMeter" })]
    [InlineData(SearchSort.Updated, SearchDirection.Ascending, new[] { "Pony-Menu", "zerp-Weapon_Fusion", "Jowday-DamageMeter" })]
    public async Task ListsInEachOrder(SearchSort sort, SearchDirection direction, string[] order)
    {
        Assert.Equal(order, Ids(await SearchAsync(new SearchQuery(sort: sort, direction: direction))));
    }

    [Fact]
    public async Task GivesAPageAtATime()
    {
        SearchPage first = await SearchAsync(new SearchQuery(count: 2));
        SearchPage second = await SearchAsync(new SearchQuery(offset: first.NextOffset, count: 2));

        Assert.Equal(["Jowday-DamageMeter", "zerp-Weapon_Fusion"], Ids(first));
        Assert.Equal(["Pony-Menu"], Ids(second));
        Assert.Equal((true, false), (first.HasMore, second.HasMore));
    }

    [Fact]
    public async Task RefusesAnotherSitesGame()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Source.SearchAsync(
            new Domain.Mods.GameRef(new Domain.Mods.SourceId("nexus"), "game"), new SearchQuery(), Cancellation));
    }

    private Task<SearchPage> SearchAsync(SearchQuery query) => _harness.Source.SearchAsync(Game, query, Cancellation);

    private static string[] Ids(SearchPage page) => [.. page.Mods.Select(mod => mod.Mod.Id)];
}
