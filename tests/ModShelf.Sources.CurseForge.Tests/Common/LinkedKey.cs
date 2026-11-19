using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Sources.CurseForge.Tests.Common;

/// <summary>
/// The app with an API key linked for CurseForge, which it adds to each request as the source's strategy does; or none.
/// </summary>
internal sealed class LinkedKey(string? key) : ICredentialProvider
{
    public const string Key = "$2a$10$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    public Task<AccountCredentials?> AuthorizeAsync(SourceId source, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (key is null)
        {
            return Task.FromResult<AccountCredentials?>(null);
        }

        request.Headers.TryAddWithoutValidation("x-api-key", key);
        return Task.FromResult<AccountCredentials?>(new AccountCredentials(key));
    }

    public Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken)
        => Task.FromResult<AccountCredentials?>(null);
}
