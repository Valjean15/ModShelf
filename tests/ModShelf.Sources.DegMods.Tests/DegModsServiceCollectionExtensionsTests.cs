using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests;

/// <summary>
/// Registering the source, as the hosts do.
/// </summary>
public sealed class DegModsServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersTheSourceOnceHoweverOftenCalled()
    {
        ServiceCollection services = new();
        services.AddLogging();

        services.AddDegModsSource();
        services.AddDegModsSource();
        using ServiceProvider provider = services.BuildServiceProvider();

        DegModsSource source = Assert.IsType<DegModsSource>(Assert.Single(provider.GetServices<IModSource>()));
        Assert.Equal(("degmods", "DEG Mods"), (source.Id.Value, source.DisplayName));
        Assert.Single(services, service => service.ServiceType == typeof(ModIndex));
        Assert.Single(services, service => service.ServiceType == typeof(RelayClient));
    }

    [Fact]
    public void ReadsDegModsOwnRelaysAndAsksTheFileServersItsSiteDoes()
    {
        DegModsOptions options = new();

        Assert.Equal(new Uri("https://degmods.com/"), options.SiteAddress);
        Assert.Equal(["wss://brs.degmods.com/", "wss://relay.degmods.com/"], options.Relays.Select(relay => relay.AbsoluteUri));
        Assert.Equal(7, options.FileServers.Count);
        Assert.Equal(["brs.degmods.com", "bs.degmods.com"], options.GatedHosts);
        Assert.Equal("f4bf1fb5ba8be839f70c7331733e309f780822b311f63e01f9dc8abbb428f8d5", options.Admin);
        Assert.Equal((TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5)), (options.AnswerTimeout, options.KeepModsFor));
    }

    [Theory]
    [InlineData("https://degmods.com/site")]
    [InlineData("ftp://degmods.com/")]
    [InlineData("/")]
    public void RefusesASiteThatCantBeDegMods(string address)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { SiteAddress = new Uri(address, UriKind.RelativeOrAbsolute) }));
    }

    [Theory]
    [InlineData("https://brs.degmods.com/")]
    [InlineData("brs.degmods.com")]
    public void RefusesARelayThatIsntAWebSocket(string relay)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { Relays = [new Uri(relay, UriKind.RelativeOrAbsolute)] }));
    }

    [Fact]
    public void RefusesNoRelays()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { Relays = [] }));
    }

    [Theory]
    [InlineData("https://blossom.primal.net/files")]
    [InlineData("wss://blossom.primal.net/")]
    public void RefusesAFileServerThatIsntAWebAddressEndingInASlash(string server)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { FileServers = [new Uri(server)] }));
    }

    [Fact]
    public void RefusesABlankGatedHost()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { GatedHosts = [" "] }));
    }

    [Theory]
    [InlineData("F4BF1FB5BA8BE839F70C7331733E309F780822B311F63E01F9DC8ABBB428F8D5")]
    [InlineData("npub17jl3ldd6305rnacvwvchx03snauqsg4nz8mruq0emj9thdpglr2sst825x")]
    [InlineData("f4bf")]
    public void RefusesAnAdminThatIsntAKeyInLowercaseHexadecimal(string admin)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { Admin = admin }));
    }

    [Fact]
    public void RefusesNoTimeToAnswerAndKeepingModsForLessThanNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { AnswerTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddDegModsSource(new DegModsOptions { KeepModsFor = TimeSpan.FromSeconds(-1) }));
        new ServiceCollection().AddDegModsSource(new DegModsOptions { KeepModsFor = TimeSpan.Zero });
    }

    [Fact]
    public async Task NamesTheAppToTheFileServers()
    {
        using RecordedDegMods degMods = new();
        using ServiceProvider services = degMods.BuildServices();
        DegModsSource source = services.GetServices<IModSource>().OfType<DegModsSource>().Single();

        await source.ResolveAsync(
            new Domain.Mods.ModFileRef(
                new Domain.Mods.ModRef(new Domain.Mods.GameRef(source.Id, "fallout: new vegas"), $"{RecordedDegMods.FalloutAuthor}:{RecordedDegMods.Newspapers}"),
                RecordedDegMods.NewspapersFile[..16]),
            handoff: null,
            TestContext.Current.CancellationToken);

        string userAgent = Assert.Single(degMods.Requests).RequestMessage!.Headers!["User-Agent"].ToString();
        Assert.StartsWith("ModShelf/", userAgent, StringComparison.Ordinal);
    }
}
