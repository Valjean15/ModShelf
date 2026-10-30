using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application;
using ModShelf.Application.Catalog;
using ModShelf.Application.Sources;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// The <see cref="IModLookup"/> the app registers, over sites the test sets up: so a screen's tests see what the app
/// does with what the sites answer, such as reading each changelog once.
/// </summary>
internal static class AppLookup
{
    public static IModLookup Over(ISourceRegistry sources)
    {
        ServiceCollection services = new();
        services.AddModLookup();
        Type lookup = services.Single(descriptor => descriptor.ServiceType == typeof(IModLookup)).ImplementationType!;
        return (IModLookup)ActivatorUtilities.CreateInstance(new OneService(sources), lookup);
    }

    private sealed class OneService(ISourceRegistry sources) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ISourceRegistry) ? sources : null;
    }
}
