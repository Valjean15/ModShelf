using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.Tests.Common;

public sealed class SizesTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(10 * 1024, "10.0 KB")]
    [InlineData(100 * 1024, "100 KB")]
    [InlineData(12_897_484, "12.3 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.00 GB")]
    [InlineData(2048L * 1024 * 1024 * 1024 * 1024, "2048 TB")]
    public void WritesSizesInPowersOf1024WithThreeFigures(long bytes, string text)
    {
        using CultureScope culture = new("en-US");

        Assert.Equal(text, Sizes.Format(bytes));
    }

    [Fact]
    public void WritesNumbersAsTheCultureDoes()
    {
        using CultureScope culture = new("de-DE");

        Assert.Equal("1,50 KB", Sizes.Format(1536));
    }
}
