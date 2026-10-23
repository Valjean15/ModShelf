using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Infrastructure.Tests.Persistence.Repositories;

public sealed class LinkedAccountRepositoryTests : IAsyncLifetime
{
    private static readonly SourceId Nexus = new("nexus");
    private static readonly SourceId ModIo = new("mod-io");

    private readonly TestLibraryDatabase _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ASavedAccountSurvivesARestart()
    {
        LinkedAccount account = new(
            Nexus,
            AuthKind.OAuthPkce,
            new AccountProfile("Jean", new Uri("https://avatars.example.com/u/15.png"), "Premium"),
            new DateTimeOffset(2026, 10, 23, 14, 30, 5, TimeSpan.FromHours(-6)));
        await _database.LinkedAccounts.SaveAsync(account, Cancellation);

        await _database.RestartAsync();

        LinkedAccount stored = Assert.Single(await _database.LinkedAccounts.ListAsync(Cancellation));
        Assert.Equal(account.Source, stored.Source);
        Assert.Equal(account.Kind, stored.Kind);
        Assert.Equal(account.Profile, stored.Profile);
        Assert.Equal(account.LinkedAt, stored.LinkedAt);
        Assert.Equal(account.LinkedAt.Offset, stored.LinkedAt.Offset);
    }

    [Fact]
    public async Task SavingAgainReplacesTheSourcesAccount()
    {
        await _database.LinkedAccounts.SaveAsync(Account(Nexus, "Jean", AuthKind.OAuthPkce, tier: "Premium"), Cancellation);
        LinkedAccount relinked = Account(Nexus, "Valjean", AuthKind.ApiKey);

        await _database.LinkedAccounts.SaveAsync(relinked, Cancellation);

        LinkedAccount stored = Assert.Single(await _database.LinkedAccounts.ListAsync(Cancellation));
        Assert.Equal("Valjean", stored.Profile.DisplayName);
        Assert.Equal(AuthKind.ApiKey, stored.Kind);
        Assert.Null(stored.Profile.Tier);
        Assert.Null(stored.Profile.AvatarUrl);
    }

    [Fact]
    public async Task ListsOneAccountPerSourceInSourceOrder()
    {
        await _database.LinkedAccounts.SaveAsync(Account(Nexus, "Jean"), Cancellation);
        await _database.LinkedAccounts.SaveAsync(Account(ModIo, "Jean"), Cancellation);

        SourceId[] sources = [.. (await _database.LinkedAccounts.ListAsync(Cancellation)).Select(account => account.Source)];

        Assert.Equal([ModIo, Nexus], sources);
    }

    [Fact]
    public async Task DeletingRemovesOnlyThatSourcesAccount()
    {
        await _database.LinkedAccounts.SaveAsync(Account(Nexus, "Jean"), Cancellation);
        await _database.LinkedAccounts.SaveAsync(Account(ModIo, "Jean"), Cancellation);

        await _database.LinkedAccounts.DeleteAsync(Nexus, Cancellation);
        await _database.LinkedAccounts.DeleteAsync(new SourceId("never-linked"), Cancellation);

        Assert.Equal(ModIo, Assert.Single(await _database.LinkedAccounts.ListAsync(Cancellation)).Source);
    }

    [Fact]
    public async Task TheTableHoldsNoCredentials()
    {
        object? columns = await _database.QueryAsync("SELECT group_concat(name, ',') FROM pragma_table_info('LinkedAccounts')");

        Assert.Equal("SourceId,Kind,DisplayName,AvatarUrl,Tier,LinkedAt", columns);
    }

    [Theory]
    [InlineData("INSERT INTO LinkedAccounts VALUES ('nexus', 99, 'Jean', NULL, NULL, '2026-10-23 00:00:00+00:00')")]
    [InlineData("INSERT INTO LinkedAccounts VALUES ('nexus', 1, ' ', NULL, NULL, '2026-10-23 00:00:00+00:00')")]
    [InlineData("INSERT INTO LinkedAccounts VALUES ('nexus', 1, 'Jean', 'not a link', NULL, '2026-10-23 00:00:00+00:00')")]
    [InlineData("INSERT INTO LinkedAccounts VALUES ('nexus', 1, 'Jean', 'file:///etc/passwd', NULL, '2026-10-23 00:00:00+00:00')")]
    public async Task ARowNoAccountCanHaveIsReportedAsDamaged(string insert)
    {
        await _database.QueryAsync(insert);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => _database.LinkedAccounts.ListAsync(Cancellation));

        Assert.Contains("nexus", exception.Message, StringComparison.Ordinal);
    }

    private static LinkedAccount Account(SourceId source, string name, AuthKind kind = AuthKind.ApiKey, string? tier = null)
        => new(source, kind, new AccountProfile(name, tier: tier), DateTimeOffset.UnixEpoch);
}
