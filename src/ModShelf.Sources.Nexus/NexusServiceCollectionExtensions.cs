using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Nexus.Accounts;
using ModShelf.Sources.Nexus.Api;

using Polly;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// Registers the Nexus Mods source. The hosts call this from their composition roots, so they never name the source's
/// classes.
/// </summary>
public static class NexusServiceCollectionExtensions
{
    private const string ApplicationName = "ModShelf";

    private static readonly string ApplicationVersion = typeof(NexusSource).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Adds <see cref="NexusSource"/> as an <see cref="IModSource"/> that links accounts with a personal API key, with its
    /// API client: every request names the app and its version, as Nexus Mods asks; requests that fail for a passing
    /// reason are retried with backoff; and requests are held back while the account's rate limit is used up. Calling
    /// this again changes nothing.
    /// </summary>
    /// <remarks>
    /// The client is the one named for the source in <c>SourceHttpClients</c>, so the host's <c>AddSourceHttpClients()</c>
    /// adds the linked account to its requests.
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
            [new NexusApiKeyStrategy()])));

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

        client.AddResilienceHandler("nexus", pipeline =>
        {
            if (settings.MaxRetryAttempts > 0)
            {
                // Server errors, 408, timeouts and broken connections. Not 429: the rate limit resets on the hour
                // at the earliest, so retrying within seconds only spends what's left of it.
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = settings.MaxRetryAttempts,
                    Delay = settings.RetryDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = arguments => ValueTask.FromResult(
                        arguments.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
                        && HttpClientResiliencePredicates.IsTransient(arguments.Outcome)),
                });
            }

            pipeline.AddTimeout(settings.AttemptTimeout);
        });

        // Inside the retries, so it sees every attempt.
        client.AddHttpMessageHandler(provider => new NexusQuotaHandler(
            provider.GetRequiredService<NexusQuota>(),
            provider.GetRequiredService<ILogger<NexusQuotaHandler>>()));

        return services;
    }

    private static void Validate(NexusOptions settings)
    {
        if (settings.ApiBaseAddress is not { IsAbsoluteUri: true } address
            || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
            || !address.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("The API's address must be an absolute HTTP or HTTPS URL ending in a slash.", nameof(settings));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(settings.MaxRetryAttempts, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.RetryDelay, TimeSpan.Zero, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.AttemptTimeout, TimeSpan.Zero, nameof(settings));
    }
}
