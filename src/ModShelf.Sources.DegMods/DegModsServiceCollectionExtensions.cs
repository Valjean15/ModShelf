using System.Net;
using System.Net.Http.Headers;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Http;
using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Catalog;

using Polly;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// Registers the DEG Mods source. The hosts call this from their composition roots, so they never name the source's
/// classes.
/// </summary>
public static class DegModsServiceCollectionExtensions
{
    private const string ApplicationName = "ModShelf";

    // The version the build was tagged with, such as 1.0.4, without the commit after its "+".
    private static readonly string ApplicationVersion =
        (typeof(DegModsSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Adds <see cref="DegModsSource"/> as an <see cref="IModSource"/>. It needs no account. It reads DEG Mods' relays
    /// over WebSockets, which open through a client of their own that retries a connection that fails for a passing
    /// reason, and asks file servers for files through the client named for the source in <c>SourceHttpClients</c>, so
    /// the host's <c>AddSourceHttpClients()</c> retries those requests, limits each attempt's time and refuses redirects.
    /// Both name the app and its version. Calling this again changes nothing.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The site or a file server isn't an absolute HTTP or HTTPS URL ending in a slash; there's no relay, or one isn't an
    /// absolute WebSocket URL; or the admin isn't a public key in lowercase hexadecimal.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A relay's time to answer isn't more than none, or the time mods are kept is less.</exception>
    public static IServiceCollection AddDegModsSource(this IServiceCollection services, DegModsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        DegModsOptions settings = options ?? new DegModsOptions();
        Validate(settings);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ModIndex)))
        {
            return services;
        }

        string userAgent = $"{ApplicationName}/{ApplicationVersion}";
        services.AddSingleton(provider => new RelayClient(
            provider.GetRequiredService<IHttpMessageHandlerFactory>(),
            settings.AnswerTimeout,
            userAgent,
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<ILogger<RelayClient>>()));
        services.AddSingleton(provider => new DegModsRelays(
            provider.GetRequiredService<RelayClient>(),
            settings.Relays,
            provider.GetRequiredService<ILogger<DegModsRelays>>()));
        services.AddSingleton(provider => new ModIndex(
            provider.GetRequiredService<DegModsRelays>(),
            settings.Admin,
            settings.KeepModsFor,
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModSource, DegModsSource>(provider => new DegModsSource(
            provider.GetRequiredService<ModIndex>(),
            settings,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<ILogger<DegModsSource>>())));

        services.AddHttpClient(SourceHttpClients.NameFor(DegModsSource.DegModsId), http =>
        {
            // The pipeline's timeout limits each attempt instead.
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, ApplicationVersion));
        });

        // The relays' WebSockets: a connection that fails for a passing reason is tried again, never one a relay refused.
        // How long a relay may take is the relay client's to say, for the connection and each answer alike.
        services
            .AddHttpClient(RelayClient.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddResilienceHandler("degmods-relays", pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldRetryAfterHeader = false,
                ShouldHandle = arguments => ValueTask.FromResult(
                    arguments.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
                    && HttpClientResiliencePredicates.IsTransient(arguments.Outcome)),
            }));

        return services;
    }

    private static void Validate(DegModsOptions settings)
    {
        if (!IsWebAddress(settings.SiteAddress))
        {
            throw new ArgumentException("DEG Mods' address must be an absolute HTTP or HTTPS URL ending in a slash.", nameof(settings));
        }

        if (settings.Relays is not { Count: > 0 } relays
            || relays.Any(relay => relay is not { IsAbsoluteUri: true } || (relay.Scheme != Uri.UriSchemeWss && relay.Scheme != Uri.UriSchemeWs)))
        {
            throw new ArgumentException("DEG Mods' relays must be one absolute WebSocket URL or more.", nameof(settings));
        }

        if (settings.FileServers is null || settings.FileServers.Any(server => !IsWebAddress(server)))
        {
            throw new ArgumentException("Each file server must be an absolute HTTP or HTTPS URL ending in a slash.", nameof(settings));
        }

        if (settings.GatedHosts is null || settings.GatedHosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("DEG Mods' own file servers must each be a host.", nameof(settings));
        }

        if (settings.Admin is not { Length: 64 } admin || !admin.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'))
        {
            throw new ArgumentException("DEG Mods' admin must be a public key in lowercase hexadecimal.", nameof(settings));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.AnswerTimeout, TimeSpan.Zero, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.KeepModsFor, TimeSpan.Zero, nameof(settings));
    }

    private static bool IsWebAddress(Uri? address)
        => address is { IsAbsoluteUri: true }
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            && address.AbsolutePath.EndsWith('/');
}
