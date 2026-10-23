using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence.Repositories;
using ModShelf.Infrastructure.Persistence;

namespace ModShelf.Infrastructure.Tests.Persistence;

/// <summary>
/// The library database as the hosts register it: the file, its migrations and the services.
/// </summary>
public sealed class LibraryDatabaseTests
{
    [Fact]
    public async Task StartingCreatesTheFolderAndTheSchema()
    {
        // A folder that doesn't exist yet, with a space and a semicolon to test the connection string's quoting.
        await using TestLibraryDatabase database = new("App Data; backup", "ModShelf", "library.db");

        await database.StartAsync();

        Assert.True(File.Exists(database.DatabasePath));
        Assert.Equal(1L, await database.QueryAsync("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DownloadJobs'"));

        await using ModShelfDbContext context = await database.CreateContextAsync();
        Assert.NotEmpty(context.Database.GetMigrations());
        Assert.Equal(
            context.Database.GetMigrations(),
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
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
        Assert.Equal(1L, await database.QueryAsync("SELECT COUNT(*) FROM __EFMigrationsHistory"));
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

    [Theory]
    [InlineData("library.db")]
    [InlineData("data/library.db")]
    public void RejectsARelativePath(string databasePath)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLibraryDatabase(databasePath));
    }
}
