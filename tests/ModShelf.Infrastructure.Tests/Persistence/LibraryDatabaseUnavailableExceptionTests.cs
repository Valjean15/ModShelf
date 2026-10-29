using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// What a change to the library database that failed after it opened says to the user.
/// </summary>
public sealed class LibraryDatabaseUnavailableExceptionTests
{
    [Theory]
    [InlineData(5, "another program is using it. Close that program, then try again.")]
    [InlineData(8, "it can't be written. Check that you can write it, and its folder.")]
    [InlineData(13, "the disk is full. Make room, then try again.")]
    public void AChangeTheDatabaseRefusedSaysWhatToDo(int code, string problem)
    {
        SqliteException refused = new($"SQLite Error {code}.", code);

        LibraryDatabaseUnavailableException? direct = LibraryDatabaseUnavailableException.FromFailedChange(refused);
        LibraryDatabaseUnavailableException? throughEf = LibraryDatabaseUnavailableException.FromFailedChange(new DbUpdateException("Saving failed.", refused));

        Assert.Equal($"The library database couldn't be changed: {problem}", direct?.Message);
        Assert.Equal(direct?.Message, throughEf?.Message);
    }

    [Fact]
    public void AnythingElseIsntTheDatabases()
    {
        Assert.Null(LibraryDatabaseUnavailableException.FromFailedChange(new IOException("The disk is gone.")));
        Assert.Null(LibraryDatabaseUnavailableException.FromFailedChange(new DbUpdateException("Saving failed.")));
    }
}
