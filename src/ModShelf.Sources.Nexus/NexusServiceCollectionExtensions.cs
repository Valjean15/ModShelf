using System.Net.Http.Headers;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Nexus.Accounts;
using ModShelf.Sources.Nexus.Api;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// Registers the Nexus Mods source. The hosts call this from their composition roots, so they never name the source's
/// classes.
/// </summary>
public static class NexusServiceCollectionExtensions
{
    private const string ApplicationName = "ModShelf";

    // The version the build was tagged with, such as 0.9.0, without the commit after its "+".
    private static readonly string ApplicationVersion =
        (typeof(NexusSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Adds <see cref="NexusSource"/> as an <see cref="IModSource"/>. It links accounts with a personal API key, and
    /// through the browser too when <see cref="NexusOptions.OAuthClientId"/> is set. Its API client names the app and
    /// its version on every request, as Nexus Mods asks, and holds requests back while the account's rate limit is used
    /// up. Calling this again changes nothing.
    /// </summary>
    /// <remarks>
    /// The client is the one named for the source in <c>SourceHttpClients</c>, so the host's <c>AddSourceHttpClients()</c>
    /// adds the linked account to its requests, retries them, limits each attempt's time and refuses redirects.
    /// </remarks>
    public static IServiceCollection AddNexusSource(this IServiceCollection services, NexusOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        NexusOptions settings = options ?? new NexusOptions();
        Validate(settings);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(NexusQuota)))
        {
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<NexusQuota>();
        services.AddSingleton<NexusApiClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModSource, NexusSource>(provider => new NexusSource(
            provider.GetRequiredService<NexusApiClient>(),
            Strategies(settings, provider),
            provider.GetRequiredService<NexusQuota>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<NexusSource>>())));

        IHttpClientBuilder client = services.AddHttpClient(NexusApiClient.ClientName, http =>
        {
            http.BaseAddress = settings.ApiBaseAddress;

            // The pipeline's timeout limits each attempt instead.
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.Add("Application-Name", ApplicationName);
            http.DefaultRequestHeaders.Add("Application-Version", ApplicationVersion);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, ApplicationVersion));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Inside the retries the host adds, so it sees every attempt.
        client.AddHttpMessageHandler(provider => new NexusQuotaHandler(
            provider.GetRequiredService<NexusQuota>(),
            provider.GetRequiredService<ILogger<NexusQuotaHandler>>()));

        return services;
    }

    /// <summary>
    /// Signing in through the browser first, once Nexus Mods has issued a client ID; the personal API key always.
    /// </summary>
    private static IAuthStrategy[] Strategies(NexusOptions settings, IServiceProvider provider)
    {
        NexusApiKeyStrategy apiKey = new();
        return settings.OAuthClientId is { } clientId
            ? [
                new NexusOAuthStrategy(
                    provider.GetRequiredService<NexusApiClient>(),
                    provider.GetRequiredService<TimeProvider>(),
                    clientId,
                    settings.UsersBaseAddress),
                apiKey,
            ]
            : [apiKey];
    }

    private static void Validate(NexusOptions settings)
    {
        if (!IsBaseAddress(settings.ApiBaseAddress) || !IsBaseAddress(settings.UsersBaseAddress))
        {
            throw new ArgumentException("The API's and the sign-in server's addresses must be absolute HTTP or HTTPS URLs ending in a slash.", nameof(settings));
        }

        if (settings.OAuthClientId is { } clientId && string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("The OAuth client ID can't be blank; leave it out to offer only the API key.", nameof(settings));
        }
    }

    private static bool IsBaseAddress(Uri address)
        => address is { IsAbsoluteUri: true }
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            && address.AbsolutePath.EndsWith('/');
}
