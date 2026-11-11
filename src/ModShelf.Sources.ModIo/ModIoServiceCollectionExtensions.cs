using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.ModIo.Api;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// Registers the mod.io source. The hosts call this from their composition roots, so they never name the source's
/// classes.
/// </summary>
public static class ModIoServiceCollectionExtensions
{
    private const string ApplicationName = "ModShelf";

    // The version the build was tagged with, such as 1.0.3, without the commit after its "+".
    private static readonly string ApplicationVersion =
        (typeof(ModIoSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Adds <see cref="ModIoSource"/> as an <see cref="IModSource"/>. It needs the user's API key or access token, which
    /// the app adds to each request. Its API client names the app and its version on every request, sends mod.io at most
    /// <see cref="ModIoOptions.RequestsPerMinute"/> a minute, nothing for a while after mod.io asks it to slow down, and
    /// never sends an API key where mod.io answers for an access token only. Calling this again changes nothing.
    /// </summary>
    /// <remarks>
    /// The client is the one named for the source in <c>SourceHttpClients</c>, so the host's <c>AddSourceHttpClients()</c>
    /// adds the linked account, retries its requests, limits each attempt's time and refuses redirects.
    /// </remarks>
    /// <exception cref="ArgumentException">An address isn't an absolute HTTP or HTTPS URL ending in a slash.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The requests a minute aren't 1 or more.</exception>
    public static IServiceCollection AddModIoSource(this IServiceCollection services, ModIoOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ModIoOptions settings = options ?? new ModIoOptions();
        Validate(settings);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ModIoApiClient)))
        {
            return services;
        }

        services.AddSingleton(provider => new ModIoPacer(provider.GetService<TimeProvider>() ?? TimeProvider.System, settings.RequestsPerMinute));
        services.AddSingleton(provider => new ModIoApiClient(provider.GetRequiredService<IHttpClientFactory>(), provider.GetRequiredService<ModIoPacer>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModSource, ModIoSource>(provider => new ModIoSource(
            provider.GetRequiredService<ModIoApiClient>(),
            settings,
            provider.GetService<TimeProvider>() ?? TimeProvider.System,
            provider.GetRequiredService<ILogger<ModIoSource>>())));

        services
            .AddHttpClient(ModIoApiClient.ClientName, http =>
            {
                // The pipeline's timeout limits each attempt instead.
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, ApplicationVersion));
            })
            .AddHttpMessageHandler(() => new ModIoTokenGate())
            .AddHttpMessageHandler(provider => new ModIoSlowDownHandler(
                provider.GetRequiredService<ModIoPacer>(),
                provider.GetRequiredService<ILogger<ModIoSlowDownHandler>>()));

        return services;
    }

    private static void Validate(ModIoOptions settings)
    {
        if (!IsApiAddress(settings.ApiAddress))
        {
            throw new ArgumentException("mod.io's API address must be an absolute HTTP or HTTPS URL ending in a slash.", nameof(settings));
        }

        string? game = null;
        try
        {
            game = string.Format(CultureInfo.InvariantCulture, settings.GameApiAddress ?? string.Empty, 1);
        }
        catch (FormatException)
        {
        }

        if (!Uri.TryCreate(game, UriKind.Absolute, out Uri? gameAddress) || !IsApiAddress(gameAddress))
        {
            throw new ArgumentException(
                "A mod.io game's API address must be an absolute HTTP or HTTPS URL ending in a slash, with {0} where the game's ID goes.",
                nameof(settings));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(settings.RequestsPerMinute, 1, nameof(settings));
    }

    private static bool IsApiAddress(Uri? address)
        => address is { IsAbsoluteUri: true }
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            && address.AbsolutePath.EndsWith('/');
}
