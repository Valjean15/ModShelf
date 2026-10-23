using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Sources.Abstractions.Tests.Links;

public sealed class RecognizedLinkTests
{
    private static readonly ModFileRef SkyUiMainFile =
        new(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407");

    [Fact]
    public void KeepsTheFileAndSuggestedName()
    {
        RecognizedLink link = new(SkyUiMainFile, "SkyUI_5_2_SE.7z");

        Assert.Same(SkyUiMainFile, link.File);
        Assert.Equal("SkyUI_5_2_SE.7z", link.SuggestedFileName);
        Assert.False(link.IsCatchAll);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsABlankSuggestionAsNone(string? suggestedFileName)
    {
        Assert.Null(new RecognizedLink(SkyUiMainFile, suggestedFileName).SuggestedFileName);
    }

    [Fact]
    public void CanBeACatchAllMatch()
    {
        Assert.True(new RecognizedLink(SkyUiMainFile, isCatchAll: true).IsCatchAll);
    }

    [Fact]
    public void RequiresAFile()
    {
        Assert.Throws<ArgumentNullException>(() => new RecognizedLink(null!));
    }
}
