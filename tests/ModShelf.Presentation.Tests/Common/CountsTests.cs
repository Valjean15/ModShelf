using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.Tests.Common;

public sealed class CountsTests
{
    [Theory]
    [InlineData(0, "0 mods")]
    [InlineData(1, "one mod")]
    [InlineData(2, "2 mods")]
    [InlineData(2_134_567, "2,134,567 mods")]
    public void SaysOneOnlyForOne(long count, string text)
    {
        using CultureScope culture = new("en-US");

        Assert.Equal(text, Counts.Of(count, "one mod", "{0:N0} mods"));
    }

    [Theory]
    [InlineData(0, "no mods")]
    [InlineData(1, "one mod")]
    [InlineData(3, "3 mods")]
    public void SaysNoneForZeroWhenGivenTheWords(long count, string text)
        => Assert.Equal(text, Counts.Of(count, "no mods", "one mod", "{0} mods"));

    [Fact]
    public void WritesTheCountAsTheCultureDoes()
    {
        using CultureScope culture = new("de-DE");

        Assert.Equal("1.234 Mods", Counts.Of(1234, "ein Mod", "{0:N0} Mods"));
    }
}
