using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Tests.Mods;

public sealed class SourceIdTests
{
    [Theory]
    [InlineData("nexus")]
    [InlineData("mod-io")]
    [InlineData("curseforge")]
    [InlineData("source2")]
    public void AcceptsLowercaseLettersDigitsAndInnerHyphens(string value)
    {
        Assert.Equal(value, new SourceId(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Nexus")]
    [InlineData("mod_io")]
    [InlineData("mod io")]
    [InlineData("-nexus")]
    [InlineData("nexus-")]
    [InlineData("nëxus")]
    public void RejectsAnythingElse(string value)
    {
        Assert.Throws<ArgumentException>(() => new SourceId(value));
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SourceId(null!));
    }

    [Fact]
    public void IdsWithTheSameValueAreEqual()
    {
        Assert.Equal(new SourceId("nexus"), new SourceId("nexus"));
        Assert.NotEqual(new SourceId("nexus"), new SourceId("modrinth"));
    }

    [Fact]
    public void ToStringIsTheValue()
    {
        Assert.Equal("nexus", new SourceId("nexus").ToString());
    }
}
