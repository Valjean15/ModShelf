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
    /// <summary>
    /// Set on a request that checks whose account credentials belong to. It goes out even while the rate limit is used
    /// up: the limit is each account's, the one used up may be another's, and the answer brings this one's.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> ChecksAccount = new("ModShelf.Nexus.ChecksAccount");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        bool checksAccount = request.Options.TryGetValue(ChecksAccount, out bool checks) && checks;
        if (!checksAccount && quota.HeldBackUntil() is { } until)
        {
            LogHeldBack(logger, until);
            return HeldBack(request, until);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        bool limitRead = quota.Update(response.Headers);
        if (response.StatusCode == HttpStatusCode.TooManyRequests && quota.HeldBackUntil() is null)
        {
            // Refused without headers saying the allowances are used up: hold back as long as the API asks. When the
            // headers show allowance left, something else that limits bursts refused it, so only briefly.
            if (limitRead)
            {
                quota.HoldBriefly(response.Headers.RetryAfter);
            }
            else
            {
                quota.MarkUsedUp(response.Headers.RetryAfter);
            }
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
