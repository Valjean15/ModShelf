using Microsoft.EntityFrameworkCore;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Infrastructure.Persistence.Tables;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// The library's history in the library database's <c>LibraryEvents</c> table.
/// </summary>
internal sealed class LibraryHistoryRepository(IDbContextFactory<ModShelfDbContext> contextFactory) : ILibraryHistoryRepository
{
    private readonly IDbContextFactory<ModShelfDbContext> _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

    public async Task AddAsync(IReadOnlyList<LibraryEvent> events, DateTimeOffset keepSince, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            context.LibraryEvents.AddRange(events.Select(LibraryEventRecord.From));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await context.LibraryEvents.Where(record => record.At < keepSince).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<LibraryEvent>> ListAsync(DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            IQueryable<LibraryEventRecord> rows = context.LibraryEvents.AsNoTracking();
            if (since is { } from)
            {
                rows = rows.Where(record => record.At >= from);
            }

            List<LibraryEventRecord> records = await rows
                .OrderByDescending(record => record.At)
                .ThenBy(record => record.Sequence)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return [.. records.Select(record => record.ToEvent())];
        }
    }
}
