using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Abstractions.Watchlist;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A source that links the user's public profile by name, as GameBanana does, and lists the mods it subscribes to,
/// which ModShelf can only read. Its one member is <see cref="Member"/>.
/// </summary>
internal sealed class FakeProfileSource : IAccountLinker, IWatchlistReader
{
    public const string Member = "jean";

    public static readonly SourceId SourceId = new("fake-profile");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake profile site";

    public SiteWords Words { get; } = new() { Followed = "subscribed" };

    public bool WorksWithoutAccount => true;

    public IReadOnlyList<IAuthStrategy> Strategies { get; } = [new ProfileStrategy()];

    /// <summary>
    /// The mods the member subscribes to.
    /// </summary>
    public List<ModRef> Subscribed { get; } = [new(new GameRef(SourceId, "16951"), "440088"), new(new GameRef(SourceId, "16951"), "414644")];

    public Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken)
        => credentials.AccessToken == "1740038"
            ? Task.FromResult(new AccountProfile(Member))
            : throw new AuthenticationFailedException("No such member.");

    public Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModRef>>([.. Subscribed]);

    /// <summary>
    /// Takes the member's name, as typed, for their number.
    /// </summary>
    private sealed class ProfileStrategy : IAuthStrategy
    {
        public AuthKind Kind => AuthKind.PublicProfile;

        public Task<AuthChallenge> BeginAsync(AuthStart start, CancellationToken cancellationToken)
            => Task.FromResult(new AuthChallenge(AuthKind.PublicProfile));

        public Task<AccountCredentials> CompleteAsync(AuthChallenge challenge, string answer, CancellationToken cancellationToken)
            => answer == Member ? Task.FromResult(new AccountCredentials("1740038")) : throw new AuthenticationFailedException("No such member.");

        public void Authorize(HttpRequestMessage request, AccountCredentials credentials)
        {
        }

        public Task<AccountCredentials?> RefreshAsync(AccountCredentials expired, CancellationToken cancellationToken)
            => Task.FromResult<AccountCredentials?>(null);
    }
}
