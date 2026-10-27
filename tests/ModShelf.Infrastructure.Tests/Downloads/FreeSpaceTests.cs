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
