using System.Globalization;

using ModShelf.Domain.Downloads;

namespace ModShelf.Domain.Tests.Downloads;

public sealed class DownloadJobIdTests
{
    [Fact]
    public void RejectsAnEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => new DownloadJobId(Guid.Empty));
    }

    [Fact]
    public void IdsWithTheSameGuidAreEqual()
    {
        Guid guid = Guid.NewGuid();

        Assert.Equal(new DownloadJobId(guid), new DownloadJobId(guid));
    }

    [Fact]
    public void NewCreatesADifferentIdEachTime()
    {
        Assert.NotEqual(DownloadJobId.New(), DownloadJobId.New());
    }

    [Fact]
    public void ToStringIsTheGuid()
    {
        Guid guid = Guid.NewGuid();

        Assert.Equal(guid.ToString("D", CultureInfo.InvariantCulture), new DownloadJobId(guid).ToString());
    }
}
