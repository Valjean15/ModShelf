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
    /// <summary>
    /// What EF Core logs, at Debug, as it starts a command (<c>RelationalEventId.CommandExecuting</c>).
    /// </summary>
    private const string Executing = "Microsoft.EntityFrameworkCore.Database.Command[20100]: Executing DbCommand";

    /// <summary>
    /// The CLI's log files with the commands it starts in them, read while it runs. On Linux and macOS, .NET locks a
    /// file a process writes from every other process's reading unless that process is told not to.
    /// </summary>
    private static readonly Dictionary<string, string> CommandsLogged = new(StringComparer.Ordinal)
    {
        ["Logging__File__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "Debug",
        ["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = "true",
    };

    private readonly TempFolder _folder = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string DatabasePath => Path.Combine(_folder.Path, "library.db");

    private string LogFolder => Path.Combine(_folder.Path, "logs");

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

        using CliProcess command = CliProcess.Start(DatabasePath, ["settings", "show"], CommandsLogged);

        // Held for a second and a half from the command's first read, however long the command takes to start, which
        // is each system's own and can take most of two seconds on CI.
        await WaitForFirstReadAsync(command);
        await Task.Delay(TimeSpan.FromSeconds(1.5), Cancellation);
        await ExecuteAsync(other, "COMMIT");
        CliResult shown = await command.WaitForExitAsync(TimeSpan.FromSeconds(60), Cancellation);

        Assert.Equal(0, shown.ExitCode);
        Assert.Equal(string.Empty, shown.Errors);
        string log = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(LogFolder, "cli-*.log")), Cancellation);
        Assert.True(log.Contains("Warning ModShelf.Infrastructure.Persistence.SlowCommandLog[205]: The library database took ", StringComparison.Ordinal), log);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Cancellation);
    }

    private static async Task<string> ReadWhileWrittenAsync(string file)
    {
        await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(Cancellation);
    }

    /// <summary>
    /// Waits for the command's log file to say it has started its first library database command.
    /// </summary>
    private async Task WaitForFirstReadAsync(CliProcess command)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (!await HasLoggedAsync(Executing))
        {
            Assert.False(command.HasExited, "The command ended before reading the library database.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }

    private async Task<bool> HasLoggedAsync(string text)
    {
        if (!Directory.Exists(LogFolder))
        {
            return false;
        }

        foreach (string file in Directory.GetFiles(LogFolder, "cli-*.log"))
        {
            if ((await ReadWhileWrittenAsync(file)).Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
