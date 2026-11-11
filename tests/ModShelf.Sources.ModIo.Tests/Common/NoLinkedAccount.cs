using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.ModIo.Tests.Common;

/// <summary>
/// The app with no account linked: requests go out as the source built them.
/// </summary>
internal sealed class NoLinkedAccount : ICredentialProvider
{
    public Task<AccountCredentials?> AuthorizeAsync(SourceId source, HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);

    public Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);
}
