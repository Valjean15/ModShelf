using System.Collections.ObjectModel;

namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// What the user has to do to link an account, from <see cref="IAuthStrategy.BeginAsync"/>.
/// </summary>
/// <remarks>
/// <see cref="State"/> holds secrets such as the PKCE code verifier, so a challenge lives only in memory
/// between the two steps: never store or log it. <see cref="ToString"/> leaves the state out.
/// </remarks>
public sealed record AuthChallenge
{
    public AuthChallenge(
        AuthKind kind,
        Uri? url = null,
        string? sentTo = null,
        IReadOnlyDictionary<string, string>? state = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of authentication.");
        }

        if (url is not null && !WebUrls.IsHttpOrHttps(url))
        {
            throw new ArgumentException("The challenge's URL must be an absolute HTTP or HTTPS URL.", nameof(url));
        }

        Kind = kind;
        Url = url;
        SentTo = string.IsNullOrWhiteSpace(sentTo) ? null : sentTo;
        State = state is null
            ? ReadOnlyDictionary<string, string>.Empty
            : new Dictionary<string, string>(state, StringComparer.Ordinal).AsReadOnly();
    }

    public AuthKind Kind { get; }

    /// <summary>
    /// The page to open: for <see cref="AuthKind.OAuthPkce"/> the authorization page, for
    /// <see cref="AuthKind.ApiKey"/> the page where the user finds their key. Null when there's none.
    /// </summary>
    public Uri? Url { get; }

    /// <summary>
    /// For <see cref="AuthKind.EmailCode"/>: where the code was sent, as the site reports it, to show the user.
    /// </summary>
    public string? SentTo { get; }

    /// <summary>
    /// The strategy's own data between the two steps, such as the PKCE code verifier and the OAuth state.
    /// Secret: never store or log it.
    /// </summary>
    public IReadOnlyDictionary<string, string> State { get; }

    public override string ToString() => $"AuthChallenge {{ Kind = {Kind}, Url = {Url?.GetLeftPart(UriPartial.Path)}, SentTo = {SentTo} }}";
}
