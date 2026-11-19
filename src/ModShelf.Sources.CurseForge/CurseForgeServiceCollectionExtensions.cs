using System.Net.Http.Headers;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Catalog;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// Registers the CurseForge source. The hosts call this from their composition roots, so they never name the source's
/// classes.
/// </summary>
public static class CurseForgeServiceCollectionExtensions
{
    private const string ApplicationName = "ModShelf";

    // The version the build was tagged with, such as 1.1.0, without the commit after its "+".
    private static readonly string ApplicationVersion =
        (typeof(CurseForgeSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Adds <see cref="CurseForgeSource"/> as an <see cref="IModSource"/>. It needs the user's own API key, linked as an
    /// account. Its API client names the app and its version on every request. Calling this again changes nothing.
    /// </summary>
    /// <remarks>
    /// The client is the one named for the source in <c>SourceHttpClients</c>, so the host's <c>AddSourceHttpClients()</c>
    /// adds the linked key to every request, retries them, limits each attempt's time and refuses redirects.
    /// </remarks>
    /// <exception cref="ArgumentException">An address isn't an absolute HTTP or HTTPS URL ending in a slash.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The pages of files read aren't 1 or more.</exception>
    public static IServiceCollection AddCurseForgeSource(this IServiceCollection services, CurseForgeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        CurseForgeOptions settings = options ?? new CurseForgeOptions();
        Validate(settings.ApiAddress, nameof(settings.ApiAddress));
        Validate(settings.WebAddress, nameof(settings.WebAddress));
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxFilePages, 1, nameof(options));

        if (services.Any(descriptor => descriptor.ServiceType == typeof(CurseForgeApiClient)))
        {
            return services;
        }

        services.AddSingleton(provider => new CurseForgeApiClient(provider.GetRequiredService<IHttpClientFactory>(), settings.ApiAddress));
        services.AddSingleton(provider => new CurseForgeGames(provider.GetRequiredService<CurseForgeApiClient>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModSource, CurseForgeSource>(provider => new CurseForgeSource(
            provider.GetRequiredService<CurseForgeApiClient>(),
            provider.GetRequiredService<CurseForgeGames>(),
            settings,
            provider.GetRequiredService<ILogger<CurseForgeSource>>())));

        services.AddHttpClient(CurseForgeApiClient.ClientName, http =>
        {
            // The pipeline's timeout limits each attempt instead.
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, ApplicationVersion));
        });

        return services;
    }

    private static void Validate(Uri address, string name)
    {
        if (address is null || !address.IsAbsoluteUri || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp) || !address.AbsoluteUri.EndsWith('/'))
        {
            throw new ArgumentException($"{name} must be an absolute HTTP or HTTPS URL ending in a slash.", name);
        }
    }
}
