using System.Net;

using ModShelf.Sources.ModIo.Accounts;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Keeps a request mod.io answers for an access token only from going out with an API key: mod.io would refuse it with
/// a 401, and the app takes a 401 for linked credentials that no longer work. It answers such a request itself, with a
/// 403, and notes it (<see cref="ModIoRequests.HeldForToken"/>), so the source says an access token is needed.
/// </summary>
/// <remarks>
/// It sits after the app adds the linked credentials, inside its retries.
/// </remarks>
internal sealed class ModIoTokenGate : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.TryGetValue(ModIoRequests.NeedsToken, out bool needsToken) && needsToken
            && request.Options.TryGetValue(ModIoRequests.Sent, out ModIoCredential sent) && sent == ModIoCredential.ApiKey)
        {
            request.Options.Set(ModIoRequests.HeldForToken, true);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { RequestMessage = request });
        }

        return base.SendAsync(request, cancellationToken);
    }
}
