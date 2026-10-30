using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Web;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IDownloadResolver"/> half: a fresh download link for a file, every time one is needed.
/// </summary>
/// <remarks>
/// Premium accounts get a link straight away. Free accounts get one only with the key from an <c>nxm://</c> link,
/// which the website hands over once the user starts the download on the file's page; until then the job waits for
/// that handoff. Links are never kept: the engine asks again when one expires.
/// </remarks>
public sealed partial class NexusSource : IDownloadResolver
{
    /// <param name="file">One of this source's files.</param>
    /// <param name="handoff">
    /// An <c>nxm://</c> link to <paramref name="file"/>. Its key and expiry are sent with the request, and never
    /// logged. When Nexus Mods refuses them, for example because they expired, the account is tried without them.
    /// </param>
    /// <returns>
    /// A ticket for the first server Nexus Mods offers, or <see cref="HandoffRequired"/> with the file's page when
    /// the account may only download files started on the website.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="file"/> isn't Nexus Mods', or <paramref name="handoff"/> isn't an <c>nxm://</c> link to it.
    /// </exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">
    /// The API couldn't be reached, offered no server, or refused for another reason; with 404 when the file is gone.
    /// </exception>
    public async Task<ResolveResult> ResolveAsync(ModFileRef file, Uri? handoff, CancellationToken cancellationToken)
    {
        NexusLink own = NexusLink.ForFile(file);
        HandoffKey? key = handoff is null ? null : KeyFor(own, handoff);

        if (key is { } given)
        {
            try
            {
                return await TicketAsync(own, given, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (IsRefusedKey(exception))
            {
                // The key expired, was used up, or belongs to another account. A Premium account doesn't need it.
            }
        }

        try
        {
            return await TicketAsync(own, key: null, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            // Only Premium accounts may download files that weren't started on the website.
            return new HandoffRequired(own.HandoffPage);
        }
    }

    /// <summary>
    /// The key and expiry an <c>nxm://</c> link to <paramref name="file"/> carries, or null when it carries none, as
    /// links for Premium accounts may not.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="handoff"/> isn't an <c>nxm://</c> link to <paramref name="file"/>.</exception>
    private static HandoffKey? KeyFor(NexusLink file, Uri handoff)
    {
        if (NexusLink.Read(handoff) != file with { IsHandoff = true })
        {
            throw new ArgumentException("The handoff isn't an nxm:// link to this file.", nameof(handoff));
        }

        NameValueCollection query = HttpUtility.ParseQueryString(handoff.Query);
        return query["key"] is { Length: > 0 } key && query["expires"] is { Length: > 0 } expires && expires.All(char.IsAsciiDigit)
            ? new HandoffKey(key, expires)
            : null;
    }

    /// <summary>
    /// Whether the API refused the request because of the key it carried. Nexus Mods doesn't document how it refuses
    /// one, so any refusal that isn't about the account, the file or the rate limit counts.
    /// </summary>
    private static bool IsRefusedKey(HttpRequestException exception)
        => exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Gone;

    /// <summary>
    /// When a server's link stops working: its <c>expires</c> parameter, in seconds since 1970. Null when it doesn't say.
    /// </summary>
    private static DateTimeOffset? ExpiryOf(Uri url)
    {
        string? expires = HttpUtility.ParseQueryString(url.Query)["expires"];
        return long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
    }

    private async Task<DownloadTicket> TicketAsync(NexusLink file, HandoffKey? key, CancellationToken cancellationToken)
    {
        string path = $"{file.FileApiPath}/download_link.json";
        if (key is { } given)
        {
            path += $"?key={Uri.EscapeDataString(given.Key)}&expires={given.Expires}";
        }

        IReadOnlyList<NexusDownloadLink> servers = await _api.GetAsync<IReadOnlyList<NexusDownloadLink>>(path, cancellationToken)
            .ConfigureAwait(false);

        // The first is Nexus Mods' own content delivery network, which picks a server near the user.
        foreach (NexusDownloadLink server in servers)
        {
            if (Uri.TryCreate(server.Url, UriKind.Absolute, out Uri? url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
            {
                return new DownloadTicket(url, expiresAt: ExpiryOf(url));
            }
        }

        throw new HttpRequestException("Nexus Mods offered no server to download the file from.");
    }

    /// <summary>
    /// The key and expiry from an <c>nxm://</c> link. Secret, so <see cref="ToString"/> leaves the key out.
    /// </summary>
    private readonly record struct HandoffKey(string Key, string Expires)
    {
        public override string ToString() => $"{nameof(HandoffKey)} {{ Expires = {Expires} }}";
    }
}
