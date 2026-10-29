using ModShelf.Infrastructure.Downloads;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Downloads;

public sealed class FreeSpaceTests : IDisposable
{
    private static readonly char Separator = Path.DirectorySeparatorChar;

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void TellsTheRoomOnTheDiskOfAFolder()
    {
        Assert.True(FreeSpace.Available(_folder.Path) > 0);
    }

    [Fact]
    public void AFolderStillToBeMadeHasTheRoomOfItsDisk()
    {
        Assert.True(FreeSpace.Available(Path.Combine(_folder.Path, "Mods", "Skyrim")) > 0);
    }

    [Fact]
    public void AFolderBehindALinkIsOnTheDiskTheLinkLeadsTo()
    {
        // Such as a Downloads folder that's a link to another disk: that disk's room counts, not the link's.
        string target = Path.Combine(_folder.Path, "Elsewhere");
        Directory.CreateDirectory(target);
        string link = Path.Combine(_folder.Path, "Downloads");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("This system doesn't let the tests make links.");
        }

        string real = FreeSpace.RealPath(Path.Combine(link, "Mods", "Skyrim"));

        Assert.Equal(Path.Combine(FreeSpace.RealPath(target), "Mods", "Skyrim"), real);
        Assert.True(FreeSpace.Available(Path.Combine(link, "Mods")) > 0);
    }

    [Fact]
    public void APathWithoutLinksIsItself()
    {
        string folder = Path.Combine(_folder.Path, "Mods");
        Directory.CreateDirectory(folder);

        Assert.Equal(
            Path.Combine(FreeSpace.RealPath(_folder.Path), "Mods", "Skyrim"),
            FreeSpace.RealPath(Path.Combine(folder, "Skyrim") + Separator));
    }

    [Fact]
    public void AMountPointHoldsThePathsUnderIt()
    {
        string root = $"{Separator}";
        string home = $"{Separator}home";

        Assert.True(FreeSpace.Holds(root, $"{Separator}home{Separator}mods"));
        Assert.True(FreeSpace.Holds(home, home));
        Assert.True(FreeSpace.Holds(home, $"{home}{Separator}mods"));
        Assert.True(FreeSpace.Holds(home + Separator, $"{home}{Separator}mods"));
        Assert.False(FreeSpace.Holds(home, $"{home}2{Separator}mods"));
        Assert.False(FreeSpace.Holds($"{home}{Separator}mods", home));
    }
}
