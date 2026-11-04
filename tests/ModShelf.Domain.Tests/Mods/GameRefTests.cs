using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Mods;

/// <summary>
/// A game as text: its site's ID, then its key, as the settings and the CLI name it.
/// </summary>
public sealed class GameRefTests
{
    [Fact]
    public void AsTextItsSiteComesFirst()
    {
        Assert.Equal("nexus/stardewvalley", new GameRef(TestData.Nexus, "stardewvalley").ToString());
        Assert.Equal("mod-io/stardew-valley", new GameRef(new SourceId("mod-io"), "stardew-valley").ToString());
    }

    [Theory]
    [InlineData("nexus/stardewvalley", "nexus", "stardewvalley")]
    [InlineData(" example / stardew-valley ", "example", "stardew-valley")]
    [InlineData("gamebanana/games/6534", "gamebanana", "games/6534")]
    [InlineData("nexus/SkyrimSpecialEdition", "nexus", "SkyrimSpecialEdition")]
    public void ReadsTheTextBack(string text, string site, string key)
    {
        Assert.True(GameRef.TryParse(text, out GameRef? game));
        Assert.Equal(new GameRef(new SourceId(site), key), game);
    }

    [Fact]
    public void ReadsItsOwnTextBack()
    {
        GameRef game = new(new SourceId("mod-io"), "stardew-valley");

        Assert.True(GameRef.TryParse(game.ToString(), out GameRef? read));
        Assert.Equal(game, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stardewvalley")]
    [InlineData("/stardewvalley")]
    [InlineData("nexus/")]
    [InlineData("nexus/  ")]
    [InlineData("Nexus/stardewvalley")]
    [InlineData("nexus mods/stardewvalley")]
    public void RefusesTextWithoutASiteAndAKey(string? text)
    {
        Assert.False(GameRef.TryParse(text, out GameRef? game));
        Assert.Null(game);
    }
}
