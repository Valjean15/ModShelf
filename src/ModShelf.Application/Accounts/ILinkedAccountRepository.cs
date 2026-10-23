using ModShelf.Domain.Mods;

namespace ModShelf.Application.Accounts;

/// <summary>
/// Stores which accounts are linked, one per source, so they survive restarts. Implemented by Infrastructure.
/// </summary>
/// <remarks>Holds no secrets: the credentials live in <see cref="ISecretStore"/>.</remarks>
public interface ILinkedAccountRepository
{
    Task<IReadOnlyList<LinkedAccount>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores the account, replacing the source's earlier account if it had one.
    /// </summary>
    Task SaveAsync(LinkedAccount account, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the source's account. Does nothing when it has none.
    /// </summary>
    Task DeleteAsync(SourceId source, CancellationToken cancellationToken);
}
