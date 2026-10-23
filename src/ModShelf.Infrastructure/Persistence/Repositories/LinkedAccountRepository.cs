using Microsoft.EntityFrameworkCore;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence.Tables;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores linked accounts in the library database, one per source. Each call uses its own short-lived context.
/// </summary>
/// <remarks>Holds no secrets: the account service keeps the credentials in the secret store.</remarks>
internal sealed class LinkedAccountRepository : ILinkedAccountRepository
{
    private readonly IDbContextFactory<ModShelfDbContext> _contextFactory;

    public LinkedAccountRepository(IDbContextFactory<ModShelfDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        _contextFactory = contextFactory;
    }

    /// <exception cref="InvalidDataException">A stored row holds values no account can have.</exception>
    public async Task<IReadOnlyList<LinkedAccount>> ListAsync(CancellationToken cancellationToken)
    {
        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<LinkedAccountRecord> records = await context.LinkedAccounts
                .AsNoTracking()
                .OrderBy(record => record.SourceId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. records.Select(record => record.ToAccount())];
        }
    }

    public async Task SaveAsync(LinkedAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            LinkedAccountRecord? stored = await context.LinkedAccounts
                .FindAsync([account.Source.Value], cancellationToken)
                .ConfigureAwait(false);
            if (stored is null)
            {
                context.LinkedAccounts.Add(LinkedAccountRecord.From(account));
            }
            else
            {
                stored.CopyFrom(account);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        string sourceId = source.Value;
        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.LinkedAccounts
                .Where(record => record.SourceId == sourceId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
