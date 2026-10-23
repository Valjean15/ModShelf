using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Sources.Abstractions;

namespace ModShelf.Sources.DirectUrl;

/// <summary>
/// Registers the direct link source. The hosts call this from their composition roots, so they never name the
/// source's class.
/// </summary>
public static class DirectUrlServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="DirectUrlSource"/> as an <see cref="IModSource"/>: plain web links, with no mod site involved.
    /// Calling this twice still registers it once.
    /// </summary>
    public static IServiceCollection AddDirectUrlSource(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModSource, DirectUrlSource>());
        return services;
    }
}
