using Microsoft.Extensions.DependencyInjection;

using ModShelf.Sources.Abstractions;

namespace ModShelf.Sources.DirectUrl.Tests;

public sealed class DirectUrlServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersTheSourceOnceAsAnIModSource()
    {
        ServiceCollection services = new();

        services.AddDirectUrlSource();
        services.AddDirectUrlSource();

        ServiceDescriptor registration = Assert.Single(services);
        Assert.Equal(typeof(IModSource), registration.ServiceType);
        Assert.Equal(typeof(DirectUrlSource), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }
}
