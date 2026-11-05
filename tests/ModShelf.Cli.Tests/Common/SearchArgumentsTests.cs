using ModShelf.Cli.Common;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// The filters <c>mod search</c> and <c>mod_search</c> read the same way: how many days back, from the start of the hour,
/// and a category by its name in any case.
/// </summary>
public sealed class SearchArgumentsTests
{
    private static readonly GameInfo Skyrim = new(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "Skyrim Special Edition", categories: ["Armour", "Audio"]);

    [Theory]
    [InlineData(1, "2026-11-03T21:00:00Z")]
    [InlineData(7, "2026-10-28T21:00:00Z")]
    [InlineData(SearchArguments.MostDays, "1926-11-29T21:00:00Z")]
    public void CountsTheDaysBackFromTheStartOfTheHourInUtc(int days, string since)
        => Assert.Equal(
            DateTimeOffset.Parse(since, System.Globalization.CultureInfo.InvariantCulture),
            SearchArguments.UpdatedSince(new DateTimeOffset(2026, 11, 4, 22, 59, 59, 999, TimeSpan.FromHours(1)), days));

    [Fact]
    public void TheResultIsInUtc()
        => Assert.Equal(TimeSpan.Zero, SearchArguments.UpdatedSince(new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.FromHours(-5)), 1).Offset);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SearchArguments.MostDays + 1)]
    public void RefusesDaysThatCantBe(int days)
        => Assert.Throws<ArgumentOutOfRangeException>(() => SearchArguments.UpdatedSince(DateTimeOffset.UtcNow, days));

    [Theory]
    [InlineData("Armour", "Armour")]
    [InlineData(" audio ", "Audio")]
    [InlineData("ARMOUR", "Armour")]
    public void FindsTheCategoryInAnyCaseAsTheGameNamesIt(string asked, string named)
        => Assert.Equal(named, SearchArguments.Category(asked, Skyrim, "Nexus Mods"));

    [Fact]
    public void RefusesACategoryTheGameDoesntHaveNamingThoseItHas()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => SearchArguments.Category(" Armor ", Skyrim, "Nexus Mods"));

        Assert.Equal("Skyrim Special Edition has no category 'Armor' on Nexus Mods. Its categories: Armour, Audio.", refused.Message);
    }

    [Fact]
    public void SaysWhenTheGameHasNoCategories()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => SearchArguments.Category("Armour", new GameInfo(Skyrim.Game, "Skyrim Special Edition"), "Nexus Mods"));

        Assert.Equal("Skyrim Special Edition has no categories on Nexus Mods.", refused.Message);
    }
}
