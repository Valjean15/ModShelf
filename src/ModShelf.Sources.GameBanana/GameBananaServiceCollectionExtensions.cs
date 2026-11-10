using System.Net.Http.Headers;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.GameBanana.Api;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// Registers the GameBanana source. The hosts call this from their composition roots, so they never name the source's
/// classes.
/// </summary>
public static class GameBananaServiceCollectionExtensions
{
    private const string ApplicationName = "ModShelf";

    // The version the build was tagged with, such as 1.0.2, without the commit after its "+".
    private static readonly string ApplicationVersion =
        (typeof(GameBananaSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Adds <see cref="GameBananaSource"/> as an <see cref="IModSource"/>. It needs no account. Its API client names the
    /// app and its version on every request, and sends a few at a time at most. Calling this again changes nothing.
    /// </summary>
    /// <remarks>
    /// The client is the one named for the source in <c>SourceHttpClients</c>, so the host's <c>AddSourceHttpClients()</c>
    /// retries its requests, limits each attempt's time and refuses redirects.
    /// </remarks>
    /// <exception cref="ArgumentException">The address isn't an absolute HTTP or HTTPS URL ending in a slash.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The requests at once aren't 1 or more.</exception>
    public static IServiceCollection AddGameBananaSource(this IServiceCollection services, GameBananaOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        GameBananaOptions settings = options ?? new GameBananaOptions();
        Validate(settings);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(GameBananaApiClient)))
        {
            return services;
        }

        services.AddSingleton(provider => new GameBananaApiClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            settings.MaxRequestsAtOnce));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModSource, GameBananaSource>(provider => new GameBananaSource(
            provider.GetRequiredService<GameBananaApiClient>(),
            provider.GetRequiredService<ILogger<GameBananaSource>>())));

        services.AddHttpClient(GameBananaApiClient.ClientName, http =>
        {
            http.BaseAddress = settings.BaseAddress;

            // The pipeline's timeout limits each attempt instead.
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, ApplicationVersion));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }

    private static void Validate(GameBananaOptions settings)
    {
        if (settings.BaseAddress is not { IsAbsoluteUri: true } address
            || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
            || !address.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("GameBanana's address must be an absolute HTTP or HTTPS URL ending in a slash.", nameof(settings));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxRequestsAtOnce, 1, nameof(settings));
    }
}
