using System.Net;
using System.Net.Http.Json;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Http;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The <see cref="FakeAccountSite"/>'s API as a fake source calls it: through the HTTP client the app sets up for that
/// source, as a real source does, so the app's handler adds the linked account and refreshes it.
/// </summary>
internal sealed class FakeSiteApi(FakeAccountSite site, IHttpClientFactory clients, SourceId source)
{
    public FakeAccountSite Site => site;

    public HttpClient CreateClient() => clients.CreateClient(SourceHttpClients.NameFor(source));

    /// <summary>
    /// Asks the site whose account the request carries.
    /// </summary>
    /// <param name="authorize">
    /// Adds the credentials being checked, while linking. Null for a request as the source sends it once linked, which
    /// leaves the credentials to the app.
    /// </param>
    /// <exception cref="AuthenticationFailedException">The site refused the request's credentials, or it had none.</exception>
    public async Task<AccountProfile> GetProfileAsync(Action<HttpRequestMessage>? authorize, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(site.BaseAddress, "api/me"));
        authorize?.Invoke(request);
        using HttpResponseMessage response = await CreateClient().SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new AuthenticationFailedException("The site refused the credentials.");
        }

        response.EnsureSuccessStatusCode();
        Me me = await response.Content.ReadFromJsonAsync<Me>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The site answered with no account.");
        return new AccountProfile(me.Name, tier: me.Membership);
    }

    private sealed record Me(string Name, string? Membership);
}
