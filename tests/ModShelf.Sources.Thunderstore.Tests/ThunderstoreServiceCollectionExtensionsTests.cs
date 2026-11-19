using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Thunderstore.Tests.Common;

namespace ModShelf.Sources.Thunderstore.Tests;

/// <summary>
/// <c>AddThunderstoreSource()</c>: the source once, its client naming ModShelf, and its options checked.
/// </summary>
public sealed class ThunderstoreServiceCollectionExtensionsTests : IDisposable
{
    private readonly ThunderstoreServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public void AddsTheSourceOnce()
    {
        ServiceCollection services = new();
        services.AddThunderstoreSource();
        services.AddThunderstoreSource();

        using ServiceProvider provider = services.AddLogging().BuildServiceProvider();

        Assert.Single(provider.GetServices<IModSource>().OfType<ThunderstoreSource>());
    }

    [Fact]
    public async Task NamesModShelfItsVersionAndItsPageInEachRequest()
    {
        _server.ServeCommunities(("game", "The Game"));
        using ServiceProvider services = _server.BuildServices();

        await ThunderstoreServer.SourceOf(services).GetGamesAsync(TestContext.Current.CancellationToken);

        string agent = Assert.Single(_server.Requests).RequestMessage!.Headers!["User-Agent"].Single();
        Assert.Matches(@"^ModShelf/\d+\.\d+\.\d+.* \(\+https://github\.com/Valjean15/ModShelf\)$", agent);
    }

    [Theory]
    [InlineData("https://thunderstore.io/api")]
    [InlineData("ftp://thunderstore.io/")]
    public void RefusesAnAddressThatIsntAFolderOverHttp(string address)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddThunderstoreSource(new ThunderstoreOptions { BaseAddress = new Uri(address) }));
    }

    [Fact]
    public void RefusesNoRequestsAtOnceOrNoLifetime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddThunderstoreSource(new ThunderstoreOptions { MaxRequestsAtOnce = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddThunderstoreSource(new ThunderstoreOptions { ListingLifetime = TimeSpan.Zero }));
    }
}
