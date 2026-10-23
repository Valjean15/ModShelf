using System.Collections.Concurrent;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Infrastructure.Tests.Fakes;

/// <summary>
/// A credential provider holding one set of credentials per source. It adds them the way a strategy might: a bearer
/// token, an <see cref="AccountHeader"/>, and a query parameter, so tests can see each one replaced rather than
/// repeated. Refreshing does whatever the test scripts, and every refusal it hears about is recorded.
/// </summary>
internal sealed class ScriptedCredentialProvider : ICredentialProvider
{
    public const string AccountHeader = "X-Account";

    public ConcurrentDictionary<SourceId, AccountCredentials> Current { get; } = new();

    public ConcurrentQueue<(SourceId Source, AccountCredentials Rejected)> Refusals { get; } = new();

    /// <summary>
    /// What a refresh returns. By default nothing: the account has to be linked again.
    /// </summary>
    public Func<AccountCredentials, Task<AccountCredentials?>> Refresh { get; set; } = _ => Task.FromResult<AccountCredentials?>(null);

    public Task<AccountCredentials?> AuthorizeAsync(SourceId source, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!Current.TryGetValue(source, out AccountCredentials? credentials))
        {
            return Task.FromResult<AccountCredentials?>(null);
        }

        request.Headers.Authorization = new("Bearer", credentials.AccessToken);
        request.Headers.Add(AccountHeader, source.Value);
        request.RequestUri = new UriBuilder(request.RequestUri!) { Query = $"{request.RequestUri!.Query.TrimStart('?')}&account={credentials.AccessToken}".TrimStart('&') }.Uri;
        return Task.FromResult<AccountCredentials?>(credentials);
    }

    public async Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken)
    {
        Refusals.Enqueue((source, rejected));
        AccountCredentials? refreshed = await Refresh(rejected);
        if (refreshed is not null)
        {
            Current[source] = refreshed;
        }

        return refreshed;
    }
}
