using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.ModIo.Tests.Common;

/// <summary>
/// The app with an API key or an access token linked: each request gets it as the source's own strategy adds it. A
/// request mod.io refuses with a 401 has the app refresh the credentials, which can't be: the app would then ask for the
/// account to be linked again, which <see cref="Refreshes"/> counts.
/// </summary>
internal sealed class LinkedCredentials(IServiceProvider services, string secret) : ICredentialProvider
{
    private int _refreshes;

    /// <summary>
    /// How many times the app was asked to refresh the credentials, as after a 401: each would have it ask for the
    /// account to be linked again.
    /// </summary>
    public int Refreshes => _refreshes;

    public Task<AccountCredentials?> AuthorizeAsync(SourceId source, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        IAccountLinker linker = services.GetServices<IModSource>().OfType<IAccountLinker>().Single(linking => linking.Id == source);
        AccountCredentials credentials = new(secret);
        linker.Strategies.Single(strategy => strategy.Kind == AuthKind.ApiKey).Authorize(request, credentials);
        return Task.FromResult<AccountCredentials?>(credentials);
    }

    public Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _refreshes);
        return Task.FromResult<AccountCredentials?>(null);
    }
}
