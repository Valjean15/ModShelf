using ModShelf.Domain.Common;

namespace ModShelf.Domain.Tests.Common;

/// <summary>
/// Whether two paths name the same place, as the system they're on compares them.
/// </summary>
public sealed class FilePathsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ModShelf.Tests"));

    [Fact]
    public void APathIsTheSameWithOrWithoutATrailingSeparator()
    {
        Assert.True(FilePaths.Same(Path.Combine(Root, "Mods"), Path.Combine(Root, "Mods") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void APathIsTheSameWrittenWithDotsInIt()
    {
        Assert.True(FilePaths.Same(Path.Combine(Root, "Mods", "a.7z"), Path.Combine(Root, "Mods", "..", "Mods", ".", "a.7z")));
    }

    [Fact]
    public void CaseCountsWhereTheSystemSaysItDoes()
    {
        bool ignoresCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(ignoresCase, FilePaths.Same(Path.Combine(Root, "Mods", "a.7z"), Path.Combine(Root, "MODS", "A.7Z")));
        Assert.Equal(ignoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, FilePaths.Comparison);
    }

    [Fact]
    public void OtherPathsArentTheSame()
    {
        Assert.False(FilePaths.Same(Path.Combine(Root, "Mods", "a.7z"), Path.Combine(Root, "Mods", "b.7z")));
        Assert.False(FilePaths.Same(Path.Combine(Root, "Mods"), Path.Combine(Root, "Mods2")));
    }
}
