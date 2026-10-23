using System.Net;
using System.Net.Http.Headers;

using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// Keeps <see cref="NexusQuota"/> up to date from every response, and holds requests back while the rate limit is used
/// up, so the account isn't refused over and over. A request held back gets a 429 response without being sent.
/// </summary>
/// <remarks>Sits inside the retry pipeline, so it sees every attempt.</remarks>
internal sealed partial class NexusQuotaHandler(NexusQuota quota, ILogger<NexusQuotaHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (quota.HeldBackUntil() is { } until)
        {
            LogHeldBack(logger, until);
            return HeldBack(request, until);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        quota.Update(response.Headers);
        if (response.StatusCode == HttpStatusCode.TooManyRequests && quota.HeldBackUntil() is null)
        {
            // Refused without headers saying the allowances are used up: hold back as long as the API asks.
            quota.MarkUsedUp(response.Headers.RetryAfter);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            LogRefusedForRateLimit(logger, quota.HeldBackUntil());
        }

        return response;
    }

    private static HttpResponseMessage HeldBack(HttpRequestMessage request, DateTimeOffset until) => new(HttpStatusCode.TooManyRequests)
    {
        RequestMessage = request,
        ReasonPhrase = "Held back by ModShelf: the rate limit is used up",
        Headers = { RetryAfter = new RetryConditionHeaderValue(until) },
    };
}
