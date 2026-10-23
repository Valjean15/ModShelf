using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Infrastructure.Tests.Persistence;

public sealed class LibraryDatabaseLocationTests
{
    [Fact]
    public void TheDefaultIsInTheUsersLocalAppData()
    {
        string path = LibraryDatabaseLocation.GetDefaultPath();

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModShelf", "library.db"),
            path);
    }
}
