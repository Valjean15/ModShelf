using System.Net;

using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Keeps to mod.io's request to slow down: after a 429 it sends nothing until mod.io's <c>retry-after</c> has passed, or
/// a minute when it says 0 or nothing, and answers each request meanwhile with a 429 of its own, never sent. mod.io
/// warns that asking on after a 429 can get the credentials revoked.
/// </summary>
/// <remarks>
/// It sits inside the app's retries, which never retry a 429, so no attempt goes out while mod.io is left alone.
/// <see cref="ModIoApiClient"/> waits each request's turn before it, so a wait doesn't count against an attempt's time.
/// </remarks>
internal sealed partial class ModIoSlowDownHandler(ModIoPacer pacer, ILogger<ModIoSlowDownHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (pacer.HeldUntil is not null)
        {
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request };
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            LogSlowingDown(logger, response.Headers.RetryAfter switch
            {
                { Delta: { } delta } => pacer.Hold(delta),
                { Date: { } date } => pacer.Hold(date),
                _ => pacer.Hold(TimeSpan.Zero),
            });
        }

        return response;
    }
}
