using System.Diagnostics.CodeAnalysis;

using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Http;

/// <summary>
/// Names the <see cref="HttpClient"/> each source uses for its site's API. A source asks
/// <c>IHttpClientFactory</c> for the client named <see cref="NameFor"/> its ID, and the app sets that client up:
/// every request gets the linked account's credentials, refreshed when they expire or are refused.
/// </summary>
/// <remarks>
/// Requests a source makes while the app is linking its account or refreshing its credentials, from
/// <see cref="Accounts.IAuthStrategy"/> and <see cref="Accounts.IAccountLinker.GetProfileAsync"/>, go out as the
/// source built them: the strategy attaches the credentials it is checking itself.
/// </remarks>
public static class SourceHttpClients
{
    private const string Prefix = "ModShelf.Source.";

    /// <summary>
    /// The name of the client for <paramref name="source"/>'s API.
    /// </summary>
    public static string NameFor(SourceId source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Prefix + source.Value;
    }

    /// <summary>
    /// The source a client name from <see cref="NameFor"/> belongs to.
    /// </summary>
    /// <returns>False for any other client name.</returns>
    public static bool TryGetSource(string? clientName, [NotNullWhen(true)] out SourceId? source)
    {
        source = null;
        if (clientName is null || !clientName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            source = new SourceId(clientName[Prefix.Length..]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
