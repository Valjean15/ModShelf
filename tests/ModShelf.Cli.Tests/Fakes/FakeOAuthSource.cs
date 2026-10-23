using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A source that needs an account, linked in the browser with OAuth, on the <see cref="FakeAccountSite"/>.
/// </summary>
internal sealed class FakeOAuthSource : IFakeAccountSource
{
    public static readonly SourceId SourceId = new("fake-oauth");

    private readonly FakeSiteApi _api;
    private readonly FakeOAuthStrategy _strategy;

    public FakeOAuthSource(FakeAccountSite site, IHttpClientFactory clients, TimeProvider timeProvider)
    {
        _api = new FakeSiteApi(site, clients, SourceId);
        _strategy = new FakeOAuthStrategy(_api, timeProvider);
        Strategies = [_strategy];
    }

    public SourceId Id => SourceId;

    public string DisplayName => "Fake OAuth site";

    public bool WorksWithoutAccount => false;

    public IReadOnlyList<IAuthStrategy> Strategies { get; }

    public Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, CancellationToken cancellationToken)
        => _api.GetProfileAsync(request => _strategy.Authorize(request, credentials), cancellationToken);

    public Task<AccountProfile> WhoAmIAsync(CancellationToken cancellationToken) => _api.GetProfileAsync(authorize: null, cancellationToken);
}
