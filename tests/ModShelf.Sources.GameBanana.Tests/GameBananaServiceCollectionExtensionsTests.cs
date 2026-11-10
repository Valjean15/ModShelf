using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// Registering the source, as the hosts do.
/// </summary>
public sealed class GameBananaServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersTheSourceOnceHoweverOftenCalled()
    {
        ServiceCollection services = new();
        services.AddLogging();

        services.AddGameBananaSource();
        services.AddGameBananaSource();
        using ServiceProvider provider = services.BuildServiceProvider();

        GameBananaSource source = Assert.IsType<GameBananaSource>(Assert.Single(provider.GetServices<IModSource>()));
        Assert.Equal(("gamebanana", "GameBanana"), (source.Id.Value, source.DisplayName));
        Assert.Equal(("like", "likes", false), (source.Words.Approval, source.Words.Approvals, source.Words.CanFollow));
    }

    [Theory]
    [InlineData("https://gamebanana.com/api")]
    [InlineData("ftp://gamebanana.com/")]
    [InlineData("/apiv11/")]
    public void RefusesAnAddressThatCantBeGameBananas(string address)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddGameBananaSource(
            new GameBananaOptions { BaseAddress = new Uri(address, UriKind.RelativeOrAbsolute) }));
    }

    [Fact]
    public void RefusesNoRequestsAtOnce()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddGameBananaSource(new GameBananaOptions { MaxRequestsAtOnce = 0 }));
    }
}
