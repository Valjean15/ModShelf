using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Tests.Catalog;

public sealed class ModRequirementTests
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");

    [Fact]
    public void KeepsWhatTheAuthorListed()
    {
        Uri page = new("https://www.nexusmods.com/stardewvalley/mods/2400");

        ModRequirement requirement = new(" SMAPI - Stardew Modding API ", Smapi, page, " For every file. ");

        Assert.Equal("SMAPI - Stardew Modding API", requirement.Name);
        Assert.Equal(Smapi, requirement.Mod);
        Assert.Equal(page, requirement.PageUrl);
        Assert.Equal("For every file.", requirement.Notes);
    }

    [Fact]
    public void AThingOffTheSiteHasNoMod()
    {
        ModRequirement requirement = new("Script Extender", pageUrl: new Uri("https://example.com/extender"), notes: " ");

        Assert.Null(requirement.Mod);
        Assert.Null(requirement.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void NeedsAName(string name)
        => Assert.ThrowsAny<ArgumentException>(() => new ModRequirement(name));

    [Fact]
    public void RefusesALinkThatIsntAWebOne()
        => Assert.Throws<ArgumentException>(() => new ModRequirement("Extender", pageUrl: new Uri("file:///C:/extender.exe")));
}
