using ModShelf.Domain.Downloads;

namespace ModShelf.Domain.Tests.Downloads;

public sealed class DownloadSpeedTests
{
    [Theory]
    [InlineData("2MB", 2 * 1024 * 1024)]
    [InlineData("512KB", 512 * 1024)]
    [InlineData(" 1.5 mb/s ", 1536 * 1024)]
    [InlineData("16kb", DownloadSpeed.Minimum)]
    [InlineData("10000MB", DownloadSpeed.Maximum)]
    [InlineData("100.6KB", 100 * 1024)]
    public void ReadsKilobytesOrMegabytesASecond(string text, long bytesPerSecond)
        => Assert.Equal(bytesPerSecond, DownloadSpeed.Parse(text));

    [Theory]
    [InlineData("2")]
    [InlineData("2GB")]
    [InlineData("15KB")]
    [InlineData("10001MB")]
    [InlineData("-1MB")]
    [InlineData("1,5MB")]
    [InlineData("fastMB")]
    [InlineData("")]
    public void RefusesWhatIsntASpeedInRange(string text)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => DownloadSpeed.Parse(text));

        Assert.StartsWith($"'{text}' isn't a speed. Use kilobytes or megabytes a second, such as 512KB or 2MB, from 16KB to 10000MB.", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2 * 1024 * 1024, "2MB")]
    [InlineData(1536 * 1024, "1536KB")]
    [InlineData((512 * 1024) + 100, "512KB")]
    public void WritesWholeMegabytesOrElseKilobytes(long bytesPerSecond, string text)
    {
        Assert.Equal(text, DownloadSpeed.Format(bytesPerSecond));
        Assert.Equal(bytesPerSecond / 1024 * 1024, DownloadSpeed.Parse(text));
    }
}
