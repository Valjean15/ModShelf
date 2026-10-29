using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Application.Accounts;

/// <summary>
/// The <see cref="ICredentialProvider"/> half: adding credentials to requests, and refreshing them.
/// </summary>
internal sealed partial class AccountService
{
    /// <summary>
    /// Credentials this close to expiring are refreshed before use, so a request doesn't start with a token
    /// that runs out on the way.
    /// </summary>
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The source whose account the current call flow is linking or refreshing, if any. Requests that flow sends to
    /// the same source are left alone: the strategy adds the credentials it is checking itself, and waiting for the
    /// source's lock, which a refresh holds, would never end.
    /// </summary>
    private static readonly AsyncLocal<SourceId?> ManagedSource = new();

    public async Task<AccountCredentials?> AuthorizeAsync(SourceId source, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        if (source == ManagedSource.Value
            || await GetUsableAsync(source, cancellationToken).ConfigureAwait(false) is not { } entry)
        {
            return null;
        }

        if (StrategyFor(source, entry.Kind) is not { } strategy)
        {
            LogCannotAuthorize(_logger, source, entry.Kind);
            return null;
        }

        strategy.Authorize(request, entry.Credentials);
        return entry.Credentials;
    }

    public async Task<AccountCredentials?> RefreshAsync(SourceId source, AccountCredentials rejected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rejected);

        using (await _vault.LockAsync(source, cancellationToken).ConfigureAwait(false))
        {
            VaultEntry? entry = await _vault.GetAsync(source, cancellationToken).ConfigureAwait(false);
            if (Usable(entry) is not { } current)
            {
                return null;
            }

            // Another request refreshed them already, or the account was linked again: use those.
            return current == rejected
                ? await RefreshAsync(source, entry!, beforeUse: false, cancellationToken).ConfigureAwait(false)
                : current;
        }
    }

    private static AccountCredentials? Usable(VaultEntry? entry) => entry is { NeedsRelink: false } ? entry.Credentials : null;

    /// <summary>
    /// Runs <paramref name="work"/> as linking or refreshing <paramref name="source"/>'s account, so the requests it
    /// sends to that source are left alone.
    /// </summary>
    private static async Task<T> WhileManagingAsync<T>(SourceId source, Func<Task<T>> work)
    {
        SourceId? outer = ManagedSource.Value;
        ManagedSource.Value = source;
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            ManagedSource.Value = outer;
        }
    }

    /// <summary>
    /// The source's entry with credentials that are good to use, refreshed first when they're about to expire.
    /// </summary>
    /// <returns>Null when there are no credentials to use.</returns>
    private async Task<VaultEntry?> GetUsableAsync(SourceId source, CancellationToken cancellationToken)
    {
        // Most requests find good credentials in memory and never wait for the lock.
        if (_vault.TryGetCached(source, out VaultEntry? cached) && cached is { NeedsRelink: false } && !IsAboutToExpire(cached.Credentials))
        {
            return cached;
        }

        using (await _vault.LockAsync(source, cancellationToken).ConfigureAwait(false))
        {
            VaultEntry? entry = await _vault.GetAsync(source, cancellationToken).ConfigureAwait(false);
            if (entry is not { NeedsRelink: false })
            {
                return null;
            }

            // Another request may have refreshed them while this one waited for the lock.
            if (!IsAboutToExpire(entry.Credentials))
            {
                return entry;
            }

            AccountCredentials? refreshed = await RefreshAsync(source, entry, beforeUse: true, cancellationToken).ConfigureAwait(false);
            return refreshed is null ? null : entry with { Credentials = refreshed };
        }
    }

    /// <summary>
    /// Refreshes the entry's credentials. Call while holding the source's lock.
    /// </summary>
    /// <param name="beforeUse">
    /// Whether the credentials still work for a moment. If so, a refresh that fails for any reason other than the
    /// site refusing it returns them unchanged, so a network hiccup doesn't block the request.
    /// </param>
    private async Task<AccountCredentials?> RefreshAsync(SourceId source, VaultEntry entry, bool beforeUse, CancellationToken cancellationToken)
    {
        if (StrategyFor(source, entry.Kind) is not { } strategy)
        {
            LogNoStrategy(_logger, source, entry.Kind);
            return beforeUse ? entry.Credentials : null;
        }

        AccountCredentials? refreshed;
        try
        {
            refreshed = await WhileManagingAsync(source, () => strategy.RefreshAsync(entry.Credentials, cancellationToken))
                .ConfigureAwait(false);
        }
        catch (AuthenticationFailedException)
        {
            refreshed = null;
        }
        catch (Exception exception) when (beforeUse && exception is not OperationCanceledException)
        {
            LogRefreshFailed(_logger, exception, source);
            return entry.Credentials;
        }

        if (refreshed is null)
        {
            _vault.MarkNeedsRelink(source);
            LogNeedsRelink(_logger, source);
            return null;
        }

        try
        {
            await _vault.SaveAsync(source, entry with { Credentials = refreshed }, CancellationToken.None).ConfigureAwait(false);
            LogRefreshed(_logger, source);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The site may have retired the credentials refreshed, so the new ones are kept, while ModShelf runs at least.
            _vault.Keep(source, entry with { Credentials = refreshed });
            LogCantSaveRefreshed(_logger, exception, source);
        }

        return refreshed;
    }

    private IAuthStrategy? StrategyFor(SourceId source, AuthKind kind)
        => _sources.Find<IAccountLinker>(source)?.Strategies.FirstOrDefault(offered => offered.Kind == kind);

    private bool IsAboutToExpire(AccountCredentials credentials)
        => credentials.ExpiresAt is { } expiresAt && expiresAt - _timeProvider.GetUtcNow() <= RefreshBeforeExpiry;
}
