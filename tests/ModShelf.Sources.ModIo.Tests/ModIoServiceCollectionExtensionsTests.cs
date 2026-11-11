using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.ModIo.Api;

namespace ModShelf.Sources.ModIo.Tests;

/// <summary>
/// Registering the source, as the hosts do.
/// </summary>
public sealed class ModIoServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersTheSourceOnceHoweverOftenCalled()
    {
        ServiceCollection services = new();
        services.AddLogging();

        services.AddModIoSource();
        services.AddModIoSource();
        using ServiceProvider provider = services.BuildServiceProvider();

        ModIoSource source = Assert.IsType<ModIoSource>(Assert.Single(provider.GetServices<IModSource>()));
        Assert.Equal(("modio", "mod.io"), (source.Id.Value, source.DisplayName));
        Assert.Single(services, service => service.ServiceType == typeof(ModIoApiClient));
        Assert.Single(services, service => service.ServiceType == typeof(ModIoPacer));
    }

    [Fact]
    public void CallsModIoAsItAsksAppsTo()
    {
        Assert.Equal(new Uri("https://api.mod.io/v1/"), new ModIoOptions().ApiAddress);
        Assert.Equal("https://g-{0}.modapi.io/v1/", new ModIoOptions().GameApiAddress);
        Assert.Equal(60, new ModIoOptions().RequestsPerMinute);
    }

    [Theory]
    [InlineData("https://api.mod.io/v1")]
    [InlineData("ftp://api.mod.io/v1/")]
    [InlineData("/v1/")]
    public void RefusesAnApiAddressThatCantBeModIos(string address)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddModIoSource(
            new ModIoOptions { ApiAddress = new Uri(address, UriKind.RelativeOrAbsolute) }));
    }

    [Theory]
    [InlineData("https://g-{0}.modapi.io/v1")]
    [InlineData("https://g-{1}.modapi.io/v1/")]
    [InlineData("ftp://g-{0}.modapi.io/v1/")]
    [InlineData("g-{0}.modapi.io/v1/")]
    [InlineData("")]
    public void RefusesAGameApiAddressThatCantBeModIos(string address)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddModIoSource(new ModIoOptions { GameApiAddress = address }));
    }

    [Fact]
    public void RefusesNoRequestsAMinute()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddModIoSource(new ModIoOptions { RequestsPerMinute = 0 }));
    }
}
