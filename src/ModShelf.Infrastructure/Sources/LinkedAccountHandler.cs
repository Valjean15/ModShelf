using System.Net;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Infrastructure.Sources;

/// <summary>
/// Adds the source's linked account to each request on its API client. After a 401 it refreshes the credentials once
/// and sends the request again; if the refresh fails, the caller gets the 401.
/// </summary>
/// <remarks>
/// <see cref="ICredentialProvider"/> decides what is added, and which requests are left as they are. Before a second
/// attempt the request gets back the headers and address it had before the first credentials were added, so the
/// refreshed credentials replace the refused ones instead of joining them.
/// </remarks>
internal sealed class LinkedAccountHandler(SourceId source, ICredentialProvider credentials) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Uri? address = request.RequestUri;
        KeyValuePair<string, IEnumerable<string>>[] headers = [.. request.Headers];
        if (request.Content is not null)
        {
            // A refused request is sent twice, so its body has to be readable twice. API requests are small.
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        AccountCredentials? sent = await credentials.AuthorizeAsync(source, request, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || sent is null)
        {
            return response;
        }

        AccountCredentials? refreshed;
        try
        {
            refreshed = await credentials.RefreshAsync(source, sent, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        if (refreshed is null)
        {
            // The account has to be linked again; the caller sees why.
            return response;
        }

        response.Dispose();
        request.RequestUri = address;
        request.Headers.Clear();
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        await credentials.AuthorizeAsync(source, request, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
