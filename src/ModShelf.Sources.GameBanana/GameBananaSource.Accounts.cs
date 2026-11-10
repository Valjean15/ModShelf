using System.Net;

using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="IAccountLinker"/> half: a member linked by their public profile, read-only, for the mods they subscribe
/// to. GameBanana gives apps no way to sign a member in, so nothing on GameBanana can be changed with it.
/// </summary>
public sealed partial class GameBananaSource : IAccountLinker
{
    /// <summary>
    /// True: everything but the linked member's subscriptions works without one.
    /// </summary>
    public bool WorksWithoutAccount => true;

    public IReadOnlyList<IAuthStrategy> Strategies { get; }

    /// <summary>
    /// The member's name and picture, from their profile page.
    /// </summary>
    /// <exception cref="ArgumentException">The source doesn't offer <paramref name="kind"/>.</exception>
    /// <exception cref="AuthenticationFailedException">
    /// GameBanana has no such member, or doesn't show their profile: it's private, or they're banned.
    /// </exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused for another reason.</exception>
    public async Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (Strategies.All(offered => offered.Kind != kind))
        {
            throw new ArgumentException($"GameBanana doesn't link accounts with {kind}.", nameof(kind));
        }

        string member = GameBananaLink.IsId(credentials.AccessToken)
            ? credentials.AccessToken
            : throw new AuthenticationFailedException("That isn't a member of GameBanana: link them again by their name or profile link.");
        GameBananaProfile profile;
        try
        {
            profile = await _api.GetAsync<GameBananaProfile>($"apiv11/Member/{member}/ProfilePage", cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException missing) when (missing.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AuthenticationFailedException($"GameBanana has no member {member}.", missing);
        }

        if (profile.IsBanned || string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new AuthenticationFailedException($"GameBanana doesn't show member {member}'s profile.");
        }

        return profile.IsPrivate
            ? throw new AuthenticationFailedException($"{profile.Name.Trim()}'s profile on GameBanana is private, so ModShelf can't read it. Make it public on GameBanana, or link another.")
            : new AccountProfile(profile.Name.Trim(), Picture(profile.AvatarUrl));
    }
}
