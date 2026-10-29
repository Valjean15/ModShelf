using System.Globalization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Infrastructure.Logging;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Logging;

/// <summary>
/// The log files, on a clock the test moves, starting on 29 October 2026 at 9:00 in UTC+1.
/// </summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private static readonly TimeZoneInfo UtcPlusOne = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

    private readonly TempFolder _folder = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));

    public FileLoggerProviderTests()
    {
        _time.SetLocalTimeZone(UtcPlusOne);
    }

    private string Logs => Path.Combine(_folder.Path, "logs");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void WritesEachEntryWithWhenHowBadWhereFromAndWhat()
    {
        using (FileLoggerProvider provider = Provider())
        {
            Write(provider.CreateLogger("ModShelf.Tests"), LogLevel.Warning, "Couldn't save download job 42", eventId: 8);
        }

        Assert.Equal(
            ["2026-10-29 09:00:00.000 +01:00 Warning ModShelf.Tests[8]: Couldn't save download job 42"],
            Lines("app-20261029.log"));
    }

    [Fact]
    public void WritesAnExceptionAndEveryLineAfterTheFirstIndented()
    {
        using (FileLoggerProvider provider = Provider())
        {
            Write(provider.CreateLogger("ModShelf.Tests"), LogLevel.Error, "Two lines,\nthe second indented", new IOException("The disk is full."), 1);
        }

        string[] lines = Lines("app-20261029.log");
        Assert.Equal("2026-10-29 09:00:00.000 +01:00 Error ModShelf.Tests[1]: Two lines,", lines[0]);
        Assert.Equal("    the second indented", lines[1]);
        Assert.Equal("    System.IO.IOException: The disk is full.", lines[2]);
    }

    [Fact]
    public void KeepsEntriesWrittenBeforeInTheDay()
    {
        using (FileLoggerProvider provider = Provider())
        {
            Write(provider.CreateLogger("First"), LogLevel.Information, "One");
        }

        using (FileLoggerProvider provider = Provider())
        {
            Write(provider.CreateLogger("Second"), LogLevel.Information, "Two");
        }

        Assert.Equal(2, Lines("app-20261029.log").Length);
    }

    [Fact]
    public void StartsAFileEachDay()
    {
        using FileLoggerProvider provider = Provider();
        ILogger logger = provider.CreateLogger("ModShelf.Tests");

        Write(logger, LogLevel.Information, "Today");
        _time.Advance(TimeSpan.FromHours(15));
        Write(logger, LogLevel.Information, "Tomorrow, just after midnight here");

        Assert.Equal(["app-20261029.log", "app-20261030.log"], Files());
    }

    [Fact]
    public void AFullFileGoesOnInTheNext()
    {
        using FileLoggerProvider provider = Provider(maxFileSize: 250);
        ILogger logger = provider.CreateLogger("ModShelf.Tests");

        for (int entry = 0; entry < 5; entry++)
        {
            Write(logger, LogLevel.Information, $"Entry {entry}, which is long enough that two of them fill a file");
        }

        Assert.Equal(["app-20261029-1.log", "app-20261029-2.log", "app-20261029.log"], Files());
        Assert.All(Files(), file => Assert.True(new FileInfo(Path.Combine(Logs, file)).Length <= 250));
    }

    [Fact]
    public void AFileAnotherProcessIsWritingIsLeftToIt()
    {
        Directory.CreateDirectory(Logs);
        using FileStream other = new(Path.Combine(Logs, "app-20261029.log"), FileMode.Append, FileAccess.Write, FileShare.None);

        using (FileLoggerProvider provider = Provider())
        {
            Write(provider.CreateLogger("ModShelf.Tests"), LogLevel.Information, "Mine");
        }

        Assert.Single(Lines("app-20261029-1.log"));
    }

    [Fact]
    public void TwoProcessesOfTheAppEachWriteAFileOfTheirOwn()
    {
        using (FileLoggerProvider first = Provider())
        using (FileLoggerProvider second = Provider())
        {
            Write(first.CreateLogger("First"), LogLevel.Information, "One");
            Write(second.CreateLogger("Second"), LogLevel.Information, "Two");
            Write(first.CreateLogger("First"), LogLevel.Information, "Three");
        }

        Assert.Equal(2, Lines("app-20261029.log").Length);
        Assert.EndsWith("Two", Assert.Single(Lines("app-20261029-1.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void DeletesTheAppsFilesOlderThanAWeekOnly()
    {
        Directory.CreateDirectory(Logs);
        string[] kept = ["app-20261023.log", "app-20261023-3.log", "other-20261001.log", "app-notes.log", "app-20261001-x.log", "app-20261001.txt"];
        string[] old = ["app-20261022.log", "app-20261001-1.log"];
        foreach (string name in kept.Concat(old))
        {
            File.WriteAllText(Path.Combine(Logs, name), "earlier");
        }

        using (FileLoggerProvider provider = Provider())
        {
            Write(provider.CreateLogger("ModShelf.Tests"), LogLevel.Information, "Today");
        }

        Assert.Equal([.. kept.Append("app-20261029.log").Order(StringComparer.Ordinal)], Files());
    }

    [Fact]
    public void AFolderThatCantBeWrittenDropsTheLogsWithoutThrowing()
    {
        File.WriteAllText(Logs, "a file where the folder goes");
        using (FileLoggerProvider provider = Provider())
        {
            ILogger logger = provider.CreateLogger("ModShelf.Tests");

            Write(logger, LogLevel.Error, "Lost");
            Write(logger, LogLevel.Error, "Lost too");
            File.Delete(Logs);
            Write(logger, LogLevel.Error, "Lost for the rest of the day");
            _time.Advance(TimeSpan.FromDays(1));
            Write(logger, LogLevel.Error, "Written");
        }

        Assert.Single(Lines("app-20261030.log"));
        Assert.Equal(["app-20261030.log"], Files());
    }

    [Fact]
    public void NothingIsWrittenOnceDisposed()
    {
        FileLoggerProvider provider = Provider();
        ILogger logger = provider.CreateLogger("ModShelf.Tests");
        provider.Dispose();

        Write(logger, LogLevel.Error, "Too late");

        Assert.False(Directory.Exists(Logs));
    }

    [Fact]
    public async Task IsRegisteredWithItsFolderAndTheFileSectionsRules()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:File:LogLevel:Default"] = "Information",
                ["Logging:File:LogLevel:Quiet"] = "None",
            })
            .Build();
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(_time);
        services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")));
        services.AddLogFiles(Logs, "app");

        await using (ServiceProvider provider = services.BuildServiceProvider())
        {
            Assert.Equal(Logs, provider.GetRequiredService<LogFolder>().Path);
            ILoggerFactory loggers = provider.GetRequiredService<ILoggerFactory>();
            Write(loggers.CreateLogger("ModShelf.Tests"), LogLevel.Information, "Written");
            Write(loggers.CreateLogger("Quiet"), LogLevel.Error, "Not written");
        }

        Assert.EndsWith("Information ModShelf.Tests[0]: Written", Assert.Single(Lines("app-20261029.log")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("logs", "app")]
    [InlineData(null, "app/desktop")]
    [InlineData(null, " ")]
    public void RefusesAFolderThatIsntAbsoluteOrANameThatCantStartAFile(string? folder, string app)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FileLoggerProvider(folder ?? Logs, app, _time));
    }

    /// <summary>
    /// Logs <paramref name="message"/> as it is, as a logger's own message methods do.
    /// </summary>
    private static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null, int eventId = 0)
        => logger.Log(level, new EventId(eventId), message, exception, (text, _) => text);

    private FileLoggerProvider Provider(long maxFileSize = FileLoggerProvider.DefaultMaxFileSize) => new(Logs, "app", _time, maxFileSize);

    private string[] Lines(string file)
        => File.ReadAllText(Path.Combine(Logs, file)).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private string[] Files() => [.. Directory.GetFiles(Logs).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal)];
}
