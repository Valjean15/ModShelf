using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Http;

using Polly;

namespace ModShelf.Infrastructure.Sources;

/// <summary>
/// Sets up every client named by <see cref="SourceHttpClients.NameFor"/> as every site's API needs: its requests get the
/// source's linked account through a <see cref="LinkedAccountHandler"/>; a request that fails for a passing reason is
/// retried, with backoff; each attempt has a time limit, its answer read in full within it; and redirects aren't
/// followed. Other clients are left as they are.
/// </summary>
/// <remarks>
/// <para>
/// A filter rather than one registration per client, because sources are registered without Infrastructure knowing
/// them: any client a source asks for under its name is covered. The client factory creates every filter when it
/// starts, for any client, so the credential provider (and with it the secret store) is fetched only when a source's
/// client is set up.
/// </para>
/// <para>
/// The handlers a source adds itself, such as one that holds requests back for its site's rate limit, sit inside the
/// retries, so they see every attempt, and outside the reading of the whole answer.
/// </para>
/// </remarks>
internal sealed class SourceHttpClientFilter(SourceHttpOptions options) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            next(builder);
            if (!SourceHttpClients.TryGetSource(builder.Name, out SourceId? source))
            {
                return;
            }

            // The API answers where it's asked. A redirect is a failure: followed, it would carry the account's key to
            // wherever it pointed, and turn a POST into a GET that succeeds. Whichever handler the host chose.
            switch (builder.PrimaryHandler)
            {
                case SocketsHttpHandler sockets:
                    sockets.AllowAutoRedirect = false;
                    break;
                case HttpClientHandler classic:
                    classic.AllowAutoRedirect = false;
                    break;
            }

            // First in line, so handlers further in (such as retries) send the credentials already added, and a 401 is
            // handled once for the whole request.
            ICredentialProvider credentials = builder.Services.GetRequiredService<ICredentialProvider>();
            builder.AdditionalHandlers.Insert(0, new LinkedAccountHandler(source, credentials));
            builder.AdditionalHandlers.Insert(1, new LateAnswerHandler());
            builder.AdditionalHandlers.Insert(2, new ResilienceHandler(Pipeline(source, builder.Services)));

            // Inside each attempt's timeout, which otherwise ends once the headers arrive.
            builder.AdditionalHandlers.Add(new WholeAnswerHandler());
        };
    }

    private ResiliencePipeline<HttpResponseMessage> Pipeline(SourceId source, IServiceProvider services)
    {
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline = new()
        {
            Name = SourceHttpClients.NameFor(source),

            // The registered clock, so tests can run the waits on theirs.
            TimeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System,
        };
        if (services.GetService<ILoggerFactory>() is { } loggers)
        {
            pipeline.ConfigureTelemetry(loggers);
        }

        if (options.MaxRetryAttempts > 0)
        {
            // Server errors, 408, timeouts and broken connections. Not 429: a site's rate limit doesn't start over within
            // seconds, so retrying soon only spends what's left of it.
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,

                // A server error's Retry-After, such as a maintenance page's hour, would hold the call that long
                // between attempts; the retries keep to their own short waits instead.
                ShouldRetryAfterHeader = false,
                ShouldHandle = arguments => ValueTask.FromResult(
                    arguments.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
                    && HttpClientResiliencePredicates.IsTransient(arguments.Outcome)),
            });
        }

        return pipeline.AddTimeout(options.AttemptTimeout).Build();
    }
}
