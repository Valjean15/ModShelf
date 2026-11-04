using ModShelf.Application.Common;

namespace ModShelf.Application.Tests.Common;

public sealed class NameQueryTests
{
    private const string DragonsDogma = "Dragon's Dogma 2";
    private const string Stalker = "S.T.A.L.K.E.R. 2: Heart of Chornobyl";
    private const string ZodiacAge = "Final Fantasy XII: The Zodiac Age";
    private const string Pokemon = "Pokémon";
    private const string SpiderMan = "Marvel's Spider-Man 2";

    [Theory]
    [InlineData(DragonsDogma, "dragons dogma")]
    [InlineData(DragonsDogma, "dragon's")]
    [InlineData(DragonsDogma, "dragon’s dogma")]
    [InlineData(DragonsDogma, "dogma 2")]
    [InlineData(DragonsDogma, "2 dragon")]
    [InlineData(DragonsDogma, "DRAG")]
    [InlineData(Stalker, "stalker 2")]
    [InlineData(Stalker, "stalker")]
    [InlineData(Stalker, "s.t.a.l.k.e.r.")]
    [InlineData(Stalker, "heart chornobyl")]
    [InlineData(Stalker, "stalker2")]
    [InlineData(ZodiacAge, "zodiac")]
    [InlineData(ZodiacAge, "fantasy xii")]
    [InlineData(ZodiacAge, "final fan")]
    [InlineData(ZodiacAge, "final fantasy 12")]
    [InlineData(ZodiacAge, "final fantasy: the zodiac age")]
    [InlineData(Pokemon, "pokemon")]
    [InlineData(Pokemon, "POKÉ")]
    [InlineData(Pokemon, "Pokémon")]
    [InlineData(SpiderMan, "spider man")]
    [InlineData(SpiderMan, "spiderman")]
    [InlineData(SpiderMan, "spider-man")]
    [InlineData(SpiderMan, "marvels")]
    [InlineData("Civilization VI", "civilization 6")]
    [InlineData("Rocky II", "rocky 2")]
    [InlineData("Final Fantasy 12", "final fantasy xii")]
    [InlineData("Straße", "strasse")]
    [InlineData("Ærøskøbing", "aeroskobing")]
    [InlineData("Łódź", "lodz")]
    [InlineData("Œuvre", "oeuvre")]
    [InlineData("Đorđe", "dorde")]
    [InlineData("“Quoted” Mod", "quoted mod")]
    [InlineData("Ｆｕｌｌ ｗｉｄｔｈ", "full width")]
    public void FindsTheNameByTheStartsOfItsWords(string name, string typed)
    {
        Assert.True(NameQuery.Parse(typed).Matches(name), $"'{typed}' should find '{name}'.");
    }

    [Theory]
    [InlineData("Stardew Valley", "dew")]
    [InlineData(DragonsDogma, "ogma")]
    [InlineData(DragonsDogma, "dogma 3")]
    [InlineData(ZodiacAge, "final fantasy 13")]
    [InlineData(ZodiacAge, "fantasy xiii")]
    [InlineData("GTA V", "gta 5")]
    [InlineData("Mega Man X", "mega man 10")]
    [InlineData("Skyrim", "skyrim special")]
    [InlineData("Fallout 4", "fallout 3")]
    [InlineData(Pokemon, "mon")]
    [InlineData("Café Racer", "e racer")]
    public void FindsNoNameAWordStartsNoWordOf(string name, string typed)
    {
        Assert.False(NameQuery.Parse(typed).Matches(name), $"'{typed}' shouldn't find '{name}'.");
    }

    [Theory]
    [InlineData(Stalker, "stalker 2", NameMatch.Start)]
    [InlineData(Stalker, "s.t.a", NameMatch.Start)]
    [InlineData(Stalker, "heart", NameMatch.Words)]
    [InlineData(ZodiacAge, "final fantasy 12 the", NameMatch.Start)]
    [InlineData(ZodiacAge, "fantasy final", NameMatch.Words)]
    [InlineData(DragonsDogma, "dragons", NameMatch.Start)]
    [InlineData(DragonsDogma, "dragons 2", NameMatch.Words)]
    [InlineData(DragonsDogma, "dragons dog", NameMatch.Start)]
    [InlineData(DragonsDogma, "zzz", NameMatch.None)]
    public void SaysWhetherTheNameStartsWithIt(string name, string typed, NameMatch match)
    {
        Assert.Equal(match, NameQuery.Parse(typed).Match(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ... !! ")]
    public void NothingThatCountsFindsEveryName(string? typed)
    {
        NameQuery query = NameQuery.Parse(typed);

        Assert.True(query.IsEmpty);
        Assert.Equal(NameMatch.Start, query.Match("Skyrim"));
        Assert.Equal(NameMatch.Start, query.Match(string.Empty));
    }

    [Fact]
    public void QueriesFindingTheSameNamesAreEqual()
    {
        Assert.Equal(NameQuery.Parse("Dragon's Dogma"), NameQuery.Parse(" dragons   DOGMA! "));
        Assert.Equal(NameQuery.Parse("Dragon's Dogma").GetHashCode(), NameQuery.Parse("dragons dogma").GetHashCode());
        Assert.NotEqual(NameQuery.Parse("dogma dragons"), NameQuery.Parse("dragons dogma"));
        Assert.Equal(NameQuery.Every, NameQuery.Parse("  "));
    }

    [Fact]
    public void KeepsWhatWasTypedTrimmed()
    {
        Assert.Equal(("dogma 2", "dogma 2"), (NameQuery.Parse("  dogma 2 ").Text, NameQuery.Parse(" dogma 2").ToString()));
    }

    [Fact]
    public void FindsTheNamesThatStartWithItFirstKeepingTheirOrderOtherwise()
    {
        string[] games = ["Fallout 4", "Fantasy Life", "Final Fantasy XII: The Zodiac Age", "Fantasy General", "Skyrim"];

        Assert.Equal(
            ["Fantasy Life", "Fantasy General", "Final Fantasy XII: The Zodiac Age"],
            NameQuery.Parse("fantasy").Find(games, game => game));
        Assert.Equal(games, NameQuery.Every.Find(games, game => game));
    }

    [Theory]
    [InlineData("Stardew Valley", "stardew valley")]
    [InlineData("Stardew Valley", " Stardew  Valley ")]
    [InlineData("Stardew Valley", "StardewValley")]
    [InlineData(Stalker, "STALKER 2 - Heart of Chornobyl")]
    [InlineData(Pokemon, "Pokemon")]
    [InlineData(ZodiacAge, "Final Fantasy 12 The Zodiac Age")]
    [InlineData("Marvel’s Spider-Man", "Marvel's Spider Man")]
    [InlineData("!!!", "!!!")]
    public void SameNamesAreTheSameOnceWhatDoesntCountIsLeftOut(string first, string second)
    {
        Assert.True(NameQuery.SameName(first, second));
        Assert.True(NameQuery.SameName(second, first));
    }

    [Theory]
    [InlineData("Skyrim", "Skyrim Special Edition")]
    [InlineData("Fallout 4", "Fallout 3")]
    [InlineData("Stardew Valley", "Stardew Valley 2")]
    [InlineData("!!!", "???")]
    [InlineData("!!!", "Skyrim")]
    public void OtherNamesAreNot(string first, string second)
    {
        Assert.False(NameQuery.SameName(first, second));
    }
}
