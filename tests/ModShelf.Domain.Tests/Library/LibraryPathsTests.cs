using ModShelf.Domain.Library;

namespace ModShelf.Domain.Tests.Library;

public sealed class LibraryPathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Library");

    [Fact]
    public void ResolvesALibraryPathUnderTheRoot()
    {
        Assert.Equal(
            Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI (12604)", "SkyUI_5_2_SE.7z"),
            LibraryPaths.Resolve(Root, "nexus/skyrimspecialedition/SkyUI (12604)/SkyUI_5_2_SE.7z"));
        Assert.Equal(Path.Combine(Root, "SkyUI.7z"), LibraryPaths.Resolve(Root, "SkyUI.7z"));
    }

    [Theory]
    [InlineData("../SkyUI.7z")]
    [InlineData("nexus//SkyUI.7z")]
    [InlineData("")]
    public void ResolvesOnlyPathsInsideTheLibrary(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => LibraryPaths.Resolve(Root, relativePath));
    }

    [Fact]
    public void FindsWhereAPathIsInTheLibrary()
    {
        Assert.Equal(
            "nexus/skyrimspecialedition/SkyUI_5_2_SE.7z",
            LibraryPaths.RelativeTo(Root, Path.Combine(Root, "nexus", "skyrimspecialedition", "SkyUI_5_2_SE.7z")));
        Assert.Equal("..SkyUI.7z", LibraryPaths.RelativeTo(Root, Path.Combine(Root, "..SkyUI.7z")));
        Assert.Equal("SkyUI.7z", LibraryPaths.RelativeTo(Root + Path.DirectorySeparatorChar, Path.Combine(Root, "SkyUI.7z")));
    }

    [Fact]
    public void APathOutsideTheRootIsntInTheLibrary()
    {
        Assert.Null(LibraryPaths.RelativeTo(Root, Root));
        Assert.Null(LibraryPaths.RelativeTo(Root, Path.GetDirectoryName(Root)!));
        Assert.Null(LibraryPaths.RelativeTo(Root, Path.Combine(Path.GetDirectoryName(Root)!, "SkyUI.7z")));
        Assert.Null(LibraryPaths.RelativeTo(Root, Root + "2"));
        Assert.Null(LibraryPaths.RelativeTo(Root, Path.Combine(Root + "2", "SkyUI.7z")));
        Assert.Null(LibraryPaths.RelativeTo(Root, Path.Combine(Root, "..", "Elsewhere", "SkyUI.7z")));
    }

    [Fact]
    public void TakesAbsolutePathsOnly()
    {
        Assert.Throws<ArgumentException>(() => LibraryPaths.RelativeTo("Library", Path.Combine(Root, "SkyUI.7z")));
        Assert.Throws<ArgumentException>(() => LibraryPaths.RelativeTo(Root, "SkyUI.7z"));
        Assert.Throws<ArgumentException>(() => LibraryPaths.Resolve("Library", "SkyUI.7z"));
    }

    [Fact]
    public void CaseCountsAsItDoesForThisSystemsPaths()
    {
        string path = Path.Combine(Root.ToUpperInvariant(), "SkyUI.7z");

        string? relative = LibraryPaths.RelativeTo(Root, path);

        Assert.Equal(OperatingSystem.IsLinux() ? null : "SkyUI.7z", relative);
    }
}
