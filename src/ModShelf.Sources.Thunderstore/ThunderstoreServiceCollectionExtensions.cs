using System.Net.Http.Headers;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Thunderstore.Api;
using ModShelf.Sources.Thunderstore.Catalog;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// Registers the Thunderstore source. The hosts call this from their composition roots, so they never name the source's
/// classes.
/// </summary>
public static class ThunderstoreServiceCollectionExtensions
{
    private const string ApplicationName = "ModShelf";

    // Where to reach whoever runs ModShelf, as Thunderstore asks of nothing but a well-behaved client gives.
    private const string ApplicationPage = "(+https://github.com/Valjean15/ModShelf)";

    // The version the build was tagged with, such as 1.0.9, without the commit after its "+".
    private static readonly string ApplicationVersion =
        (typeof(ThunderstoreSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Adds <see cref="ThunderstoreSource"/> as an <see cref="IModSource"/>. It needs no account. Its API client names
    /// the app, its version and its page on every request, and sends a few at a time at most; each community's listing
    /// is kept in memory and read again at most once an hour (<see cref="ThunderstoreOptions.ListingLifetime"/>). Calling
    /// this again changes nothing.
    /// </summary>
    /// <remarks>
    /// The client is the one named for the source in <c>SourceHttpClients</c>, so the host's <c>AddSourceHttpClients()</c>
    /// retries its requests, limits each attempt's time and refuses redirects, which the source follows itself where
    /// Thunderstore sends one, to its own hosts only. The listings read the registered <see cref="TimeProvider"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">The address isn't an absolute HTTP or HTTPS URL ending in a slash.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The requests at once aren't 1 or more, or the lifetime isn't positive.</exception>
    public static IServiceCollection AddThunderstoreSource(this IServiceCollection services, ThunderstoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ThunderstoreOptions settings = options ?? new ThunderstoreOptions();
        Validate(settings);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ThunderstoreApiClient)))
        {
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new ThunderstoreApiClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            settings.BaseAddress,
            settings.MaxRequestsAtOnce));
        services.AddSingleton(provider => new ThunderstoreListings(
            provider.GetRequiredService<ThunderstoreApiClient>(),
            provider.GetRequiredService<TimeProvider>(),
            settings.ListingLifetime,
            provider.GetRequiredService<ILogger<ThunderstoreListings>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModSource, ThunderstoreSource>(provider => new ThunderstoreSource(
            provider.GetRequiredService<ThunderstoreApiClient>(),
            provider.GetRequiredService<ThunderstoreListings>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ThunderstoreSource>>())));

        services.AddHttpClient(ThunderstoreApiClient.ClientName, http =>
        {
            http.BaseAddress = settings.BaseAddress;

            // The pipeline's timeout limits each attempt instead.
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, ApplicationVersion));
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ApplicationPage));
        });

        return services;
    }

    private static void Validate(ThunderstoreOptions settings)
    {
        if (settings.BaseAddress is not { IsAbsoluteUri: true } address
            || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
            || !address.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("Thunderstore's address must be an absolute HTTP or HTTPS URL ending in a slash.", nameof(settings));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxRequestsAtOnce, 1, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.ListingLifetime, TimeSpan.Zero, nameof(settings));
    }
}
