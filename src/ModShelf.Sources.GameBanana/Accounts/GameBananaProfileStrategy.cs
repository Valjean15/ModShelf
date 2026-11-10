using System.Globalization;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana.Accounts;

/// <summary>
/// Links a GameBanana member by their public profile, named by its link or the member's name: the credentials are the
/// member's ID, which nothing secret proves, so only what anyone can see of the member is read with them.
/// </summary>
/// <remarks>
/// A request names the linked member as <c>Member/linked/</c>, which <see cref="Authorize"/> turns into the member's ID;
/// unlinked, GameBanana has no such route and answers 404. Nothing needs refreshing: a member who's gone is linked again.
/// </remarks>
internal sealed class GameBananaProfileStrategy(Func<string, CancellationToken, Task<long?>> findMember) : IAuthStrategy
{
    /// <summary>
    /// Where a request names the linked member, as in <c>apiv11/Member/linked/Subscriptions</c>.
    /// </summary>
    public const string LinkedMember = "/Member/linked/";

    public AuthKind Kind => AuthKind.PublicProfile;

    public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
        => Task.FromResult(new AuthChallenge(AuthKind.PublicProfile));

    /// <summary>
    /// The member the answer names: a profile link such as <c>https://gamebanana.com/members/1740038</c>, the member's
    /// name as GameBanana's member search finds it, or failing that a member's ID. Whether they're there to read is
    /// checked when the source looks up their profile.
    /// </summary>
    /// <exception cref="AuthenticationFailedException">The answer is a link to something else, or names no member.</exception>
    public async Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);

        string entered = answer.Trim();
        if (GameBananaLink.TryReadMember(entered, out string? linked))
        {
            return new AccountCredentials(linked
                ?? throw new AuthenticationFailedException("That link isn't to a member's profile on GameBanana, as https://gamebanana.com/members/1740038 is."));
        }

        long? found = await findMember(entered, cancellationToken).ConfigureAwait(false);
        return found is { } member ? new AccountCredentials(member.ToString(CultureInfo.InvariantCulture))
            : GameBananaLink.IsId(entered) ? new AccountCredentials(entered)
            : throw new AuthenticationFailedException($"GameBanana has no member named “{entered}”.");
    }

    /// <summary>
    /// Names the linked member where the request says <see cref="LinkedMember"/> in its path; any other request is left
    /// as it is.
    /// </summary>
    public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        if (request.RequestUri is not { } address || !GameBananaLink.IsId(credentials.AccessToken))
        {
            return;
        }

        string text = address.IsAbsoluteUri ? address.AbsoluteUri : address.OriginalString;
        int query = text.IndexOfAny(['?', '#']);
        int at = text.IndexOf(LinkedMember, 0, query < 0 ? text.Length : query, StringComparison.Ordinal);
        if (at < 0)
        {
            return;
        }

        request.RequestUri = new Uri(
            $"{text[..at]}/Member/{credentials.AccessToken}/{text[(at + LinkedMember.Length)..]}",
            address.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
    }

    public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);
}
