using Microsoft.Data.Sqlite;

using ModShelf.Cli.Tests.Common;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Hosting;

/// <summary>
/// A library database command that takes a second or more is in the log files, never on standard error, where a
/// command's own words go, and an MCP server's, and where it would read as something gone wrong (ADR 0081).
/// </summary>
public sealed class SlowCommandsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string DatabasePath => Path.Combine(_folder.Path, "library.db");

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _folder.Dispose();
    }

    [Fact]
    public async Task ACommandThatWaitsForTheDatabaseSaysNothingOfItOnStandardError()
    {
        // A database another program changes while the command starts, keeping every reader out, as a database that
        // doesn't write ahead yet does: reading what's in it waits.
        await using SqliteConnection other = new(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        await other.OpenAsync(Cancellation);
        await ExecuteAsync(other, "CREATE TABLE Another(Id INTEGER)");
        await ExecuteAsync(other, "BEGIN EXCLUSIVE");
        await ExecuteAsync(other, "INSERT INTO Another VALUES (1)");

        using CliProcess command = CliProcess.Start(DatabasePath, ["settings", "show"]);
        await Task.Delay(TimeSpan.FromSeconds(2), Cancellation);
        await ExecuteAsync(other, "COMMIT");
        CliResult shown = await command.WaitForExitAsync(TimeSpan.FromSeconds(60), Cancellation);

        Assert.Equal(0, shown.ExitCode);
        Assert.Equal(string.Empty, shown.Errors);
        string log = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(Path.Combine(_folder.Path, "logs"), "cli-*.log")), Cancellation);
        Assert.True(log.Contains("Warning ModShelf.Infrastructure.Persistence.SlowCommandLog[205]: The library database took ", StringComparison.Ordinal), log);
    }
}
