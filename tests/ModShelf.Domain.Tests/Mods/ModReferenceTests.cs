using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Mods;

/// <summary>
/// Tests for <see cref="GameRef"/>, <see cref="ModRef"/> and <see cref="ModFileRef"/>, which share their rules.
/// </summary>
public sealed class ModReferenceTests
{
    [Fact]
    public void ReferencesWithTheSameValuesAreEqual()
    {
        ModFileRef first = new(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407");
        ModFileRef second = new(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void TheSameModIdInAnotherGameIsAnotherMod()
    {
        GameRef fallout4 = new(TestData.Nexus, "fallout4");

        Assert.NotEqual(new ModRef(TestData.Skyrim, "100"), new ModRef(fallout4, "100"));
    }

    [Fact]
    public void TheSameGameKeyOnAnotherSourceIsAnotherGame()
    {
        Assert.NotEqual(new GameRef(TestData.Nexus, "skyrim"), new GameRef(new SourceId("mod-io"), "skyrim"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void RejectsABlankKeyOrId(string blank)
    {
        Assert.Throws<ArgumentException>(() => new GameRef(TestData.Nexus, blank));
        Assert.Throws<ArgumentException>(() => new ModRef(TestData.Skyrim, blank));
        Assert.Throws<ArgumentException>(() => new ModFileRef(TestData.SkyUi, blank));
    }

    [Fact]
    public void RejectsAMissingParent()
    {
        Assert.Throws<ArgumentNullException>(() => new GameRef(null!, "skyrimspecialedition"));
        Assert.Throws<ArgumentNullException>(() => new ModRef(null!, "12604"));
        Assert.Throws<ArgumentNullException>(() => new ModFileRef(null!, "35407"));
    }
}
