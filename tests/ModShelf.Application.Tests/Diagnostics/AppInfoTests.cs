using ModShelf.Application.Diagnostics;

namespace ModShelf.Application.Tests.Diagnostics;

/// <summary>
/// The version and commit a build carries in its informational version, as MinVer and the .NET SDK write it.
/// </summary>
public sealed class AppInfoTests
{
    [Theory]
    [InlineData("0.9.0+a60d76b07192e034bdef7911503686277195f71d", "0.9.0", "a60d76b")]
    [InlineData("0.9.1-alpha.0.3+a60d76b", "0.9.1-alpha.0.3", "a60d76b")]
    [InlineData("0.9.1-alpha.0.3+abc", "0.9.1-alpha.0.3", "abc")]
    [InlineData("0.9.0-alpha.0", "0.9.0-alpha.0", null)]
    [InlineData("0.9.0+", "0.9.0", null)]
    [InlineData(" 1.0.0 ", "1.0.0", null)]
    [InlineData("", "0.0.0", null)]
    [InlineData(null, "0.0.0", null)]
    public void ReadsTheVersionAndTheCommitsStart(string? informational, string version, string? commit)
    {
        Assert.Equal(new AppInfo(version, commit), AppInfo.Parse(informational));
    }

    [Fact]
    public void ReadsAnAssemblysVersionAsItsBuildGaveIt()
    {
        AppInfo app = AppInfo.Of(typeof(AppInfo).Assembly);

        // Versioned from the repository's tags, at least 0.9 before the first.
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", app.Version);
        Assert.DoesNotContain("+", app.Version, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAsTheVersionAndTheCommit()
    {
        Assert.Equal("0.9.0 (a60d76b)", new AppInfo("0.9.0", "a60d76b").ToString());
        Assert.Equal("0.9.0", new AppInfo("0.9.0", null).ToString());
    }
}
