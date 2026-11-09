using ModShelf.Application.Catalog.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Infrastructure.Tests.Persistence.Repositories;

public sealed class ModDetailsRepositoryTests : IAsyncLifetime
{
    private static readonly ModRef Smapi = new(new GameRef(new SourceId("nexus"), "stardewvalley"), "2400");
    private static readonly DateTimeOffset SavedAt = new(2026, 11, 2, 16, 30, 5, TimeSpan.FromHours(-6));

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task SavedDetailsSurviveARestart()
    {
        ModInfo details = new(
            Smapi,
            "SMAPI - Stardew Modding API",
            summary: "The mod loader for Stardew Valley.",
            author: "Pathoschild",
            version: "4.3.2",
            pageUrl: new Uri("https://www.nexusmods.com/stardewvalley/mods/2400"),
            pictureUrl: new Uri("https://staticdelivery.nexusmods.com/mods/1303/images/2400/2400-1.png"),
            updatedAt: new DateTimeOffset(2026, 10, 30, 9, 15, 0, TimeSpan.FromHours(2)),
            isAvailable: false,
            hasAdultContent: true,
            downloads: 81_000_000,
            endorsements: 1_300_000,
            thumbnailUrl: new Uri("https://staticdelivery.nexusmods.com/mods/1303/images/thumbnails/2400/2400-1.png"),
            thumbnailBlurredUrl: new Uri("https://staticdelivery.nexusmods.com/mods/1303/images/thumbnails/2400/2400-1-blurred.png"));
        await _database.ModDetails.SaveAsync(new SavedModDetails(details, SavedAt), Cancellation);

        await _database.RestartAsync();

        SavedModDetails? stored = await _database.ModDetails.FindAsync(Smapi, Cancellation);
        Assert.NotNull(stored);
        Assert.Equal(details, stored.Details);
        Assert.Equal(details.UpdatedAt!.Value.Offset, stored.Details.UpdatedAt!.Value.Offset);
        Assert.Equal(SavedAt, stored.SavedAt);
        Assert.Equal(SavedAt.Offset, stored.SavedAt.Offset);
    }

    [Fact]
    public async Task SavingAgainReplacesTheModsDetails()
    {
        ModInfo first = new(
            Smapi,
            "SMAPI",
            summary: "A summary.",
            author: "Pathoschild",
            pictureUrl: new Uri("https://staticdelivery.nexusmods.com/mods/1303/images/2400/2400-1.png"),
            downloads: 10);
        ModInfo second = new(Smapi, "SMAPI - Stardew Modding API");
        await _database.ModDetails.SaveAsync(new SavedModDetails(first, SavedAt), Cancellation);

        await _database.ModDetails.SaveAsync(new SavedModDetails(second, SavedAt.AddDays(1)), Cancellation);

        SavedModDetails? stored = await _database.ModDetails.FindAsync(Smapi, Cancellation);
        Assert.Equal(new SavedModDetails(second, SavedAt.AddDays(1)), stored);
        Assert.Equal(1L, await _database.QueryAsync("SELECT COUNT(*) FROM ModDetails"));
    }

    [Fact]
    public async Task TellsModsApartBySourceGameAndId()
    {
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(Smapi, "SMAPI"), SavedAt), Cancellation);

        ModRef[] others =
        [
            new(Smapi.Game, "2401"),
            new(new GameRef(Smapi.Game.Source, "stardewvalley2"), "2400"),
            new(new GameRef(new SourceId("mod-io"), "stardewvalley"), "2400"),
            new(new GameRef(Smapi.Game.Source, "StardewValley"), "2400"),
        ];

        foreach (ModRef other in others)
        {
            Assert.Null(await _database.ModDetails.FindAsync(other, Cancellation));
        }

        Assert.NotNull(await _database.ModDetails.FindAsync(Smapi, Cancellation));
    }

    [Fact]
    public async Task SaysWhenSavedDetailsCantBeRead()
    {
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(Smapi, "SMAPI"), SavedAt), Cancellation);
        await _database.QueryAsync("UPDATE ModDetails SET PictureUrl = 'javascript:alert(1)'");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() => _database.ModDetails.FindAsync(Smapi, Cancellation));

        Assert.Contains("mod 2400 of stardewvalley on nexus", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsADatabaseThatCantHoldDetailsRatherThanThrowing()
    {
        await _database.QueryAsync("DROP TABLE ModDetails");

        SavedModDetails? found = await _database.ModDetails.FindAsync(Smapi, Cancellation);
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(Smapi, "SMAPI"), SavedAt), Cancellation);

        Assert.Null(found);
        string[] logged = [.. _database.Logs.Entries.Where(entry => entry.Contains("ModDetailsRepository", StringComparison.Ordinal))];
        Assert.Equal(2, logged.Length);
        Assert.Contains("couldn't be read from the library database", logged[0], StringComparison.Ordinal);
        Assert.Contains("couldn't be saved in the library database", logged[1], StringComparison.Ordinal);
        Assert.All(logged, entry => Assert.Contains("ModId=2400", entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindsTheSavedDetailsOfManyModsAtOnce()
    {
        ModRef contentPatcher = new(Smapi.Game, "1915");
        ModRef skyUi = new(new GameRef(new SourceId("nexus"), "skyrimspecialedition"), "12604");
        ModRef unsaved = new(Smapi.Game, "9999");
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(Smapi, "SMAPI"), SavedAt), Cancellation);
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(contentPatcher, "Content Patcher"), SavedAt), Cancellation);
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(skyUi, "SkyUI"), SavedAt), Cancellation);
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(new ModRef(Smapi.Game, "4000"), "Not asked"), SavedAt), Cancellation);

        IReadOnlyDictionary<ModRef, SavedModDetails> found = await _database.ModDetails.FindAsync([Smapi, contentPatcher, skyUi, unsaved, Smapi], Cancellation);

        Assert.Equal(
            new Dictionary<ModRef, string> { [Smapi] = "SMAPI", [contentPatcher] = "Content Patcher", [skyUi] = "SkyUI" },
            found.ToDictionary(pair => pair.Key, pair => pair.Value.Details.Name));
        Assert.Equal(SavedAt, found[skyUi].SavedAt);
        Assert.Empty(await _database.ModDetails.FindAsync([], Cancellation));
    }

    [Fact]
    public async Task LeavesOutSavedDetailsThatCantBeReadAmongMany()
    {
        ModRef contentPatcher = new(Smapi.Game, "1915");
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(Smapi, "SMAPI"), SavedAt), Cancellation);
        await _database.ModDetails.SaveAsync(new SavedModDetails(new ModInfo(contentPatcher, "Content Patcher"), SavedAt), Cancellation);
        await _database.QueryAsync("UPDATE ModDetails SET PictureUrl = 'javascript:alert(1)' WHERE ModId = '2400'");

        IReadOnlyDictionary<ModRef, SavedModDetails> found = await _database.ModDetails.FindAsync([Smapi, contentPatcher], Cancellation);

        Assert.Equal([contentPatcher], found.Keys);
        Assert.Contains(_database.Logs.Entries, entry => entry.Contains("The saved details of mod 2400 of stardewvalley on nexus can't be read, so they're left out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogsADatabaseThatCantBeReadForManyModsRatherThanThrowing()
    {
        await _database.QueryAsync("DROP TABLE ModDetails");

        IReadOnlyDictionary<ModRef, SavedModDetails> found = await _database.ModDetails.FindAsync([Smapi, new ModRef(Smapi.Game, "1915")], Cancellation);

        Assert.Empty(found);
        Assert.Contains(_database.Logs.Entries, entry => entry.Contains("The saved details of 2 mods couldn't be read from the library database", StringComparison.Ordinal));
    }
}
