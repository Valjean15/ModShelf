using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// A source that works without an account, and links one with a personal API key, on the <see cref="FakeAccountSite"/>.
/// </summary>
internal sealed class FakeApiKeySource : IFakeAccountSource
{
    public static readonly SourceId SourceId = new("fake-key");

    private readonly FakeSiteApi _api;
    private readonly FakeApiKeyStrategy _strategy;

    public FakeApiKeySource(FakeAccountSite site, IHttpClientFactory clients)
    {
        _api = new FakeSiteApi(site, clients, SourceId);
        _strategy = new FakeApiKeyStrategy(site.KeyPage);
        Strategies = [_strategy];
    }

    public SourceId Id => SourceId;

    public string DisplayName => "Fake API key site";

    public bool WorksWithoutAccount => true;

    public IReadOnlyList<IAuthStrategy> Strategies { get; }

    public Task<AccountProfile> GetProfileAsync(AccountCredentials credentials, AuthKind kind, CancellationToken cancellationToken)
        => kind == _strategy.Kind
            ? _api.GetProfileAsync(request => _strategy.Authorize(request, credentials), cancellationToken)
            : throw new ArgumentException($"This source links only with {_strategy.Kind}.", nameof(kind));

    public Task<AccountProfile> WhoAmIAsync(CancellationToken cancellationToken) => _api.GetProfileAsync(authorize: null, cancellationToken);
}
