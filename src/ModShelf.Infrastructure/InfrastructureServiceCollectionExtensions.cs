using System.Net.Http.Headers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

using ModShelf.Application.Downloads;
using ModShelf.Infrastructure.Downloads;

using Polly;

namespace ModShelf.Infrastructure;

/// <summary>
/// Registers Infrastructure's services. The hosts call these from their composition roots.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    private static readonly string Version = typeof(HttpFileTransfer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Registers <see cref="HttpFileTransfer"/> as the <see cref="IFileTransfer"/>, with an HTTP client that
    /// retries transient failures with exponential backoff. The pipeline has no overall timeout on purpose:
    /// a large download can take hours, and stalls are caught by <see cref="FileTransferOptions.ReadTimeout"/>.
    /// </summary>
    public static IServiceCollection AddHttpFileTransfer(this IServiceCollection services, FileTransferOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        FileTransferOptions settings = options ?? new FileTransferOptions();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(settings);
        services.AddSingleton<IFileTransfer, HttpFileTransfer>();

        services
            .AddHttpClient(
                HttpFileTransfer.HttpClientName,
                client => client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ModShelf", Version)))
            .AddResilienceHandler("file-transfer", pipeline =>
            {
                if (settings.MaxRetryAttempts > 0)
                {
                    // Retries 5xx, 408, 429 and connection errors, honouring Retry-After. Expired links
                    // (401, 403, 404, 410) aren't transient, so they come straight back to the scheduler.
                    pipeline.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = settings.MaxRetryAttempts,
                        Delay = settings.RetryDelay,
                    });
                }
            });

        return services;
    }
}
