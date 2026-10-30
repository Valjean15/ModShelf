using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Diagnostics.Ports;
using ModShelf.Application.Downloads.Ports;
using ModShelf.Application.Library.Ports;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence;
using ModShelf.Infrastructure.Persistence.Repositories;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// The library database as the hosts register it: the file, its migrations and the services.
/// </summary>
public sealed class LibraryDatabaseTests
{
    private const string InitialCreate = "20261023144452_InitialCreate";
    private const string AddLinkedAccounts = "20261023202312_AddLinkedAccounts";

    [Fact]
    public async Task StartingCreatesTheFolderAndTheSchema()
    {
        // A folder that doesn't exist yet, with a space and a semicolon to test the connection string's quoting.
        await using TestLibraryDatabase database = new("App Data; backup", "ModShelf", "library.db");

        await database.StartAsync();

        Assert.True(File.Exists(database.DatabasePath));
        Assert.Equal(1L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DownloadJobs'"));
        Assert.Equal(1L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LinkedAccounts'"));

        await using ModShelfDbContext context = await database.CreateContextAsync();
        Assert.NotEmpty(context.Database.GetMigrations());
        Assert.Equal(
            context.Database.GetMigrations(),
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileThatIsntADatabaseStopsTheStartAndStaysAsItWas()
    {
        await using TestLibraryDatabase database = new();
        byte[] notADatabase = [.. Enumerable.Repeat((byte)'x', 4096)];
        await File.WriteAllBytesAsync(database.DatabasePath, notADatabase, TestContext.Current.CancellationToken);

        LibraryDatabaseUnavailableException refused = await Assert.ThrowsAsync<LibraryDatabaseUnavailableException>(database.StartAsync);

        Assert.StartsWith(
            $"The library database {database.DatabasePath} can't be opened: it isn't a ModShelf library database, or it's damaged.",
            refused.Message,
            StringComparison.Ordinal);
        Assert.Equal(notADatabase, await File.ReadAllBytesAsync(database.DatabasePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADatabaseThatCantBeOpenedIsntKeptOpen()
    {
        // On Windows, reading the file back, as the test above does, finds it open; Linux lets it be read, and lists it.
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux lists the files a process has open, in /proc/self/fd.");
        await using TestLibraryDatabase database = new();
        await File.WriteAllBytesAsync(database.DatabasePath, [.. Enumerable.Repeat((byte)'x', 4096)], TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<LibraryDatabaseUnavailableException>(database.StartAsync);

        Assert.Empty(OpenHandlesTo(database.DatabasePath));
    }

    [Fact]
    public async Task ADatabaseAnotherModShelfKeepsUpdatingIsntKeptOpen()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux lists the files a process has open, in /proc/self/fd.");
        await using TestLibraryDatabase database = new();
        Directory.CreateDirectory(Path.GetDirectoryName(database.DatabasePath)!);

        // A database that's there, with every migration still to apply, so that the start opens it before it waits.
        await database.QueryAsync("PRAGMA user_version = 1");
        await using FileStream held = new(database.DatabasePath + ".update.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<LibraryDatabaseUnavailableException>(
            () => Migrator(database, TimeSpan.FromMilliseconds(300)).StartingAsync(TestContext.Current.CancellationToken));

        Assert.Empty(OpenHandlesTo(database.DatabasePath));
    }

    [Fact]
    public async Task AFolderThatCantBeCreatedStopsTheStart()
    {
        await using TestLibraryDatabase database = new("App Data", "library.db");
        await File.WriteAllTextAsync(Path.GetDirectoryName(database.DatabasePath)!, "a file where the folder goes", TestContext.Current.CancellationToken);

        LibraryDatabaseUnavailableException refused = await Assert.ThrowsAsync<LibraryDatabaseUnavailableException>(database.StartAsync);

        Assert.StartsWith($"The library database {database.DatabasePath} can't be opened: its folder can't be created.", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(26, "it isn't a ModShelf library database, or it's damaged.")]
    [InlineData(11, "it isn't a ModShelf library database, or it's damaged.")]
    [InlineData(5, "another program is using it.")]
    [InlineData(6, "another program is using it.")]
    [InlineData(14, "it can't be opened.")]
    [InlineData(3, "it can't be opened.")]
    [InlineData(23, "it can't be opened.")]
    [InlineData(8, "it can't be written.")]
    [InlineData(13, "the disk is full.")]
    [InlineData(10, "SQLite Error 10: 'disk I/O error'.")]
    public void SaysWhatsWrongWithTheDatabase(int errorCode, string problem)
    {
        SqliteException exception = new($"SQLite Error {errorCode}: 'disk I/O error'.", errorCode);

        Assert.StartsWith(problem, LibraryDatabaseMigrator.Problem(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAgainKeepsTheJobs()
    {
        await using TestLibraryDatabase database = new();
        await database.StartAsync();
        DownloadJob job = DownloadJob.Create(
            new ModFileRef(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407"),
            Path.GetDirectoryName(database.DatabasePath)!,
            "SkyUI_5_2_SE.7z",
            DateTimeOffset.UnixEpoch);
        await database.Repository.AddAsync(job, TestContext.Current.CancellationToken);

        await database.RestartAsync();

        Assert.Equal(job.Id, Assert.Single(await database.Repository.ListAsync(TestContext.Current.CancellationToken)).Id);

        // Each migration ran once, on the first start.
        await using ModShelfDbContext context = await database.CreateContextAsync();
        Assert.Equal((long)context.Database.GetMigrations().Count(), await database.QueryAsync("SELECT COUNT(*) FROM __EFMigrationsHistory"));
    }

    [Fact]
    public async Task ADatabaseFromBeforeLinkedAccountsIsUpgradedWithItsJobs()
    {
        await using TestLibraryDatabase database = new();
        await using (ModShelfDbContext context = await database.CreateContextAsync())
        {
            // The schema Phase 1 shipped with.
            await context.GetService<IMigrator>().MigrateAsync(InitialCreate, TestContext.Current.CancellationToken);
        }

        DownloadJob job = DownloadJob.Create(
            new ModFileRef(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407"),
            Path.GetDirectoryName(database.DatabasePath)!,
            "SkyUI_5_2_SE.7z",
            DateTimeOffset.UnixEpoch);
        await database.Repository.AddAsync(job, TestContext.Current.CancellationToken);
        Assert.Equal(0L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LinkedAccounts'"));

        await database.StartAsync();

        Assert.Equal(1L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LinkedAccounts'"));
        Assert.Equal(job.Id, Assert.Single(await database.Repository.ListAsync(TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task ADatabaseFromBeforeTheLibraryIsUpgradedWithItsJobs()
    {
        await using TestLibraryDatabase database = new();
        await using (ModShelfDbContext context = await database.CreateContextAsync())
        {
            // The schema Phase 3 shipped with.
            await context.GetService<IMigrator>().MigrateAsync(AddLinkedAccounts, TestContext.Current.CancellationToken);
        }

        DownloadJob job = DownloadJob.Create(
            new ModFileRef(new ModRef(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604"), "35407"),
            Path.GetDirectoryName(database.DatabasePath)!,
            "SkyUI_5_2_SE.7z",
            DateTimeOffset.UnixEpoch);
        await database.Repository.AddAsync(job, TestContext.Current.CancellationToken);
        Assert.Equal(0L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'Library%'"));

        await database.StartAsync();

        Assert.Equal(2L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('LibraryMods', 'LibraryFiles')"));
        Assert.Equal(job.Id, Assert.Single(await database.Repository.ListAsync(TestContext.Current.CancellationToken)).Id);
        ModRef mod = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
        await database.Library.UpdateAsync(mod, _ => LibraryMod.Watch(mod, "SMAPI", null, null, DateTimeOffset.UnixEpoch), TestContext.Current.CancellationToken);
        Assert.Single(await database.Library.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnUpdateThatStoppedPartWayDoesntHoldTheNextOneUp()
    {
        await using TestLibraryDatabase database = new();
        await using (ModShelfDbContext context = await database.CreateContextAsync())
        {
            // The update before the latest one, and the lock the latest one left when the app stopped part-way.
            List<string> migrations = [.. context.Database.GetMigrations()];
            await context.GetService<IMigrator>().MigrateAsync(migrations[^2], TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"__EFMigrationsLock\" (\"Id\", \"Timestamp\") VALUES (1, '2026-10-29 21:00:00+00:00')",
                TestContext.Current.CancellationToken);
        }

        await database.StartAsync().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await using (ModShelfDbContext context = await database.CreateContextAsync())
        {
            Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(0L, await database.QueryAsync("SELECT COUNT(*) FROM \"__EFMigrationsLock\""));
        Assert.Contains(database.Logs.Entries, entry => entry.Contains("Cleared the lock an update of the library database left", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStartWaitsForAnotherModShelfUpdatingTheDatabase()
    {
        await using TestLibraryDatabase database = new();
        LibraryDatabaseMigrator migrator = Migrator(database, TimeSpan.FromSeconds(30));
        Task starting;
        await using (new FileStream(database.DatabasePath + ".update.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            starting = migrator.StartingAsync(TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
            Assert.False(starting.IsCompleted);
        }

        await starting.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(1L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LibraryMods'"));
    }

    [Fact]
    public async Task AStartFindsTheUpdateTheModShelfItWaitedForMade()
    {
        await using TestLibraryDatabase database = new();
        LibraryDatabaseMigrator migrator = Migrator(database, TimeSpan.FromSeconds(30));
        Task starting;
        await using (new FileStream(database.DatabasePath + ".update.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            starting = migrator.StartingAsync(TestContext.Current.CancellationToken);
            await using ModShelfDbContext context = await database.CreateContextAsync();
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await starting.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(1L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LibraryMods'"));
    }

    [Fact]
    public async Task TheUpdateLockKeepsEveryoneElseOut()
    {
        await using TestLibraryDatabase database = new();

        await using FileStream held = await Migrator(database, TimeSpan.FromSeconds(30)).TakeUpdateLockAsync(TestContext.Current.CancellationToken);

        Assert.ThrowsAny<IOException>(
            () => new FileStream(database.DatabasePath + ".update.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite));
    }

    [Fact]
    public async Task AStartStopsWhenAnotherModShelfKeepsUpdatingTheDatabase()
    {
        await using TestLibraryDatabase database = new();
        Directory.CreateDirectory(Path.GetDirectoryName(database.DatabasePath)!);
        await using FileStream held = new(database.DatabasePath + ".update.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        LibraryDatabaseUnavailableException refused = await Assert.ThrowsAsync<LibraryDatabaseUnavailableException>(
            () => Migrator(database, TimeSpan.FromMilliseconds(300)).StartingAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            $"The library database {database.DatabasePath} can't be opened: another ModShelf is updating it, and hasn't finished. Wait for it, then try again.",
            refused.Message);
    }

    [Fact]
    public async Task TheModelMatchesTheLatestMigration()
    {
        // Fails when the model changes without a new migration: run `dotnet ef migrations add <Name>`.
        await using TestLibraryDatabase database = new();
        await using ModShelfDbContext context = await database.CreateContextAsync();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task MigratesBeforeAnyHostedServiceStarts()
    {
        await using TestLibraryDatabase database = new();

        IHostedService migrator = Assert.Single(database.Services.GetServices<IHostedService>());
        IHostedLifecycleService lifecycle = Assert.IsAssignableFrom<IHostedLifecycleService>(migrator);

        // StartAsync is where other services read the database; by then StartingAsync has done the work.
        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        Assert.False(File.Exists(database.DatabasePath));

        await lifecycle.StartingAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(database.DatabasePath));
    }

    [Fact]
    public async Task RegistersTheRepository()
    {
        await using TestLibraryDatabase database = new();

        Assert.IsType<DownloadJobRepository>(database.Services.GetRequiredService<IDownloadJobRepository>());
    }

    [Fact]
    public void WithoutAPathTheDatabaseGoesToItsDefaultLocation()
    {
        ServiceCollection services = new();

        string folder = services.AddLibraryFiles(null, "app");

        ServiceDescriptor options = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(LibraryDatabaseOptions));
        Assert.Equal(LibraryDatabaseLocation.GetDefaultPath(), Assert.IsType<LibraryDatabaseOptions>(options.ImplementationInstance).DatabasePath);
        Assert.Equal(Path.GetDirectoryName(LibraryDatabaseLocation.GetDefaultPath()), folder);
    }

    [Fact]
    public void TheSettingsAndTheLogsGoBesideTheDatabase()
    {
        ServiceCollection services = new();
        string database = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Moved", "test.db");

        string folder = services.AddLibraryFiles(database, "app");

        ServiceDescriptor options = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(LibraryDatabaseOptions));
        Assert.Equal(database, Assert.IsType<LibraryDatabaseOptions>(options.ImplementationInstance).DatabasePath);
        Assert.Equal(Path.GetDirectoryName(database), folder);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Equal(Path.Combine(folder, "logs"), provider.GetRequiredService<ModShelf.Infrastructure.Logging.LogFolder>().Path);
    }

    [Fact]
    public void SaysWhereTheAppsOwnFilesAre()
    {
        ServiceCollection services = new();
        string database = Path.Combine(Path.GetTempPath(), "ModShelf.Tests", "Moved", "test.db");

        string folder = services.AddLibraryFiles(database, "app");

        using ServiceProvider provider = services.BuildServiceProvider();
        IAppFolders folders = provider.GetRequiredService<IAppFolders>();
        Assert.Equal((folder, Path.Combine(folder, "logs")), (folders.DataFolder, folders.LogFolder));
    }

    [Fact]
    public async Task RegistersTheLibraryRepository()
    {
        await using TestLibraryDatabase database = new();

        Assert.IsType<LibraryRepository>(database.Services.GetRequiredService<ILibraryRepository>());
    }

    [Fact]
    public async Task RegistersTheLinkedAccountRepository()
    {
        await using TestLibraryDatabase database = new();

        Assert.IsType<LinkedAccountRepository>(database.Services.GetRequiredService<ILinkedAccountRepository>());
    }

    [Theory]
    [InlineData("library.db")]
    [InlineData("data/library.db")]
    public void RejectsARelativePath(string databasePath)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLibraryDatabase(databasePath));
    }

    /// <summary>
    /// This process's handles open to <paramref name="path"/>, on Linux.
    /// </summary>
    private static List<string> OpenHandlesTo(string path)
        => [.. Directory.EnumerateFileSystemEntries("/proc/self/fd").Where(handle => LinkTarget(handle) == path)];

    // A handle closed while the folder is read has no target.
    private static string? LinkTarget(string handle)
    {
        try
        {
            return new FileInfo(handle).LinkTarget;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static LibraryDatabaseMigrator Migrator(TestLibraryDatabase database, TimeSpan patience)
        => new(
            database.Services.GetRequiredService<LibraryDatabaseOptions>(),
            database.Services.GetRequiredService<IDbContextFactory<ModShelfDbContext>>(),
            database.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LibraryDatabaseMigrator>>())
        {
            UpdatePatience = patience,
        };
}
