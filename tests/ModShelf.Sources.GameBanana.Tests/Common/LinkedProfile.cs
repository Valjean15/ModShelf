using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.GameBanana.Tests.Common;

/// <summary>
/// The app with a GameBanana member linked by their profile: each request gets the member as the source's own strategy
/// adds them.
/// </summary>
internal sealed class LinkedProfile(IServiceProvider services, string member) : ICredentialProvider
{
    public Task<AccountCredentials?> AuthorizeAsync(SourceId source, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        IAccountLinker linker = services.GetServices<IModSource>().OfType<IAccountLinker>().Single(linking => linking.Id == source);
        AccountCredentials credentials = new(member);
        linker.Strategies.Single(strategy => strategy.Kind == AuthKind.PublicProfile).Authorize(request, credentials);
        return Task.FromResult<AccountCredentials?>(credentials);
    }

    public Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);
}
