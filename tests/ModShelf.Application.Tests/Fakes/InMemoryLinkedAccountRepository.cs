using System.Collections.Concurrent;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps linked accounts in memory, one per source.
/// </summary>
internal sealed class InMemoryLinkedAccountRepository : ILinkedAccountRepository
{
    public ConcurrentDictionary<SourceId, LinkedAccount> Accounts { get; } = new();

    public Task<IReadOnlyList<LinkedAccount>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LinkedAccount>>([.. Accounts.Values]);

    public Task SaveAsync(LinkedAccount account, CancellationToken cancellationToken)
    {
        Accounts[account.Source] = account;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(SourceId source, CancellationToken cancellationToken)
    {
        Accounts.TryRemove(source, out _);
        return Task.CompletedTask;
    }
}
