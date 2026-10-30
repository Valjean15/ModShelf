using System.Collections.Concurrent;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps linked accounts in memory, one per source. While <see cref="SaveFailure"/> is set, nothing can be saved.
/// </summary>
internal sealed class InMemoryLinkedAccountRepository : ILinkedAccountRepository
{
    public ConcurrentDictionary<SourceId, LinkedAccount> Accounts { get; } = new();

    /// <summary>
    /// What saving throws, while set, as a locked or full database does.
    /// </summary>
    public Exception? SaveFailure { get; set; }

    public Task<IReadOnlyList<LinkedAccount>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LinkedAccount>>([.. Accounts.Values]);

    public Task SaveAsync(LinkedAccount account, CancellationToken cancellationToken)
    {
        if (SaveFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        Accounts[account.Source] = account;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(SourceId source, CancellationToken cancellationToken)
    {
        Accounts.TryRemove(source, out _);
        return Task.CompletedTask;
    }
}
