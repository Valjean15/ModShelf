using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// What a slow command's log line says it was: its statement and its first table, never its values.
/// </summary>
public sealed class CommandSummaryTests
{
    [Theory]
    [InlineData("SELECT \"m\".\"Id\", \"m\".\"Name\"\nFROM \"Mods\" AS \"m\"\nWHERE \"m\".\"Source\" = @p0", "SELECT from Mods")]
    [InlineData("  select count(*) from DownloadJobs", "SELECT from DownloadJobs")]
    [InlineData("INSERT INTO \"DownloadJobs\" (\"Id\", \"Url\")\nVALUES (@p0, @p1)\nRETURNING \"RowVersion\";", "INSERT into DownloadJobs")]
    [InlineData("UPDATE \"Mods\" SET \"Name\" = @p0\nWHERE \"Id\" = @p1;", "UPDATE Mods")]
    [InlineData("DELETE FROM \"ModFiles\"\nWHERE \"Id\" = @p0;", "DELETE from ModFiles")]
    [InlineData("CREATE TABLE \"Pins\" (\"Id\" INTEGER)", "CREATE table Pins")]
    [InlineData("SELECT \"t\".\"Id\" FROM (SELECT \"Id\" FROM \"Mods\") AS \"t\"", "SELECT from Mods")]
    [InlineData("PRAGMA journal_mode", "PRAGMA")]
    [InlineData("SELECT 1", "SELECT")]
    [InlineData("", "a command")]
    [InlineData("   ", "a command")]
    [InlineData("-- a comment", "a command")]
    public void SaysTheStatementAndItsTable(string sql, string summary) => Assert.Equal(summary, CommandSummary.Of(sql));

    [Fact]
    public void NothingButATablesNameIsKept()
    {
        string summary = CommandSummary.Of("UPDATE \"Settings\" SET \"Value\" = 'from secret-key-123' WHERE \"Name\" = 'into the void'");

        Assert.Equal("UPDATE Settings", summary);
    }

    [Fact]
    public void AValueWhereATableWouldBeIsNotKept()
    {
        Assert.Equal("SELECT", CommandSummary.Of("SELECT 'x' FROM 'abc def'"));
        Assert.Equal("SELECT", CommandSummary.Of("SELECT 1 FROM (VALUES (2))"));
    }
}
