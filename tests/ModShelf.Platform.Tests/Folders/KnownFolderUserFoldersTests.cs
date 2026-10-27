using System.Runtime.Versioning;

using ModShelf.Platform.Folders;

namespace ModShelf.Platform.Tests.Folders;

[SupportedOSPlatform("windows")]
public sealed class KnownFolderUserFoldersTests
{
    [Fact]
    public void WindowsSaysWhereTheDownloadsFolderIs()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Known Folders API is Windows'.");

        string downloads = Assert.IsType<string>(KnownFolderInterop.PathOf(KnownFolderInterop.Downloads));

        Assert.True(Path.IsPathFullyQualified(downloads));
        Assert.IsType<KnownFolderUserFolders>(UserFolders.ForThisSystem());
        Assert.Equal(downloads, UserFolders.ForThisSystem().Downloads);
    }
}
