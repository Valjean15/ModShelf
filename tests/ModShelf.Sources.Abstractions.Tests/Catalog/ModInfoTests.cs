using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Tests.Catalog;

public sealed class ModInfoTests
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");

    [Fact]
    public void KeepsWhatTheSourceSays()
    {
        Uri page = new("https://www.nexusmods.com/stardewvalley/mods/2400");
        Uri picture = new("https://staticdelivery.nexusmods.com/mods/1303/images/2400/picture.png");
        DateTimeOffset updated = new(2026, 3, 14, 19, 7, 23, TimeSpan.Zero);

        ModInfo mod = new(Smapi, "SMAPI", "The mod loader.", "Pathoschild", "4.5.2", page, picture, updated, isAvailable: false, hasAdultContent: true);

        Assert.Equal(
            (Smapi, "SMAPI", "The mod loader.", "Pathoschild", "4.5.2", page, picture, (DateTimeOffset?)updated, false, true),
            (mod.Mod, mod.Name, mod.Summary, mod.Author, mod.Version, mod.PageUrl, mod.PictureUrl, mod.UpdatedAt, mod.IsAvailable, mod.HasAdultContent));
    }

    [Fact]
    public void IsAvailableAndForEveryoneUnlessTheSourceSaysOtherwise()
    {
        ModInfo mod = new(Smapi, "SMAPI");

        Assert.True(mod.IsAvailable);
        Assert.False(mod.HasAdultContent);
        Assert.Null(mod.Downloads);
        Assert.Null(mod.Endorsements);
    }

    [Fact]
    public void KeepsHowPopularTheModIs()
    {
        ModInfo mod = new(Smapi, "SMAPI", downloads: 30286114, endorsements: 387171);

        Assert.Equal((30286114L, 387171L), (mod.Downloads!.Value, mod.Endorsements!.Value));
    }

    [Fact]
    public void RefusesNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModInfo(Smapi, "SMAPI", downloads: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModInfo(Smapi, "SMAPI", endorsements: -1));
    }

    [Fact]
    public void TreatsBlankTextAsNone()
    {
        ModInfo mod = new(Smapi, "SMAPI", summary: " ", author: "", version: "\t");

        Assert.Null(mod.Summary);
        Assert.Null(mod.Author);
        Assert.Null(mod.Version);
    }

    [Fact]
    public void NeedsAName()
    {
        Assert.Throws<ArgumentException>(() => new ModInfo(Smapi, " "));
    }

    [Fact]
    public void RefusesLinksThatArentWebLinks()
    {
        Uri script = new("javascript:alert(1)");

        Assert.Throws<ArgumentException>(() => new ModInfo(Smapi, "SMAPI", pageUrl: script));
        Assert.Throws<ArgumentException>(() => new ModInfo(Smapi, "SMAPI", pictureUrl: script));
    }
}
