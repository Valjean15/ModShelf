using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Infrastructure.Persistence.Tables;

namespace ModShelf.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores the library's mods and files in the library database. Each call uses its own short-lived context.
/// </summary>
internal sealed class LibraryRepository : ILibraryRepository
{
    private readonly IDbContextFactory<ModShelfDbContext> _contextFactory;

    public LibraryRepository(IDbContextFactory<ModShelfDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        _contextFactory = contextFactory;
    }

    /// <exception cref="InvalidDataException">A stored row holds values no library mod can have.</exception>
    public async Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken)
    {
        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            List<LibraryModRecord> records = await context.LibraryMods
                .AsNoTracking()
                .Include(record => record.Files)
                .Include(record => record.Updates)
                .AsSplitQuery()
                .OrderBy(record => record.Sequence)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return [.. records.Select(record => record.ToMod())];
        }
    }

    /// <exception cref="InvalidDataException">The stored rows hold values no library mod can have.</exception>
    public async Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            LibraryModRecord? record = await Rows(context, modRef)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return record?.ToMod();
        }
    }

    /// <remarks>
    /// The transaction is SQLite's immediate kind, which takes the write lock before reading, so two changes to the
    /// library, even from two processes, run one after the other.
    /// </remarks>
    /// <exception cref="InvalidDataException">The stored rows hold values no library mod can have.</exception>
    public async Task<LibraryChange> UpdateAsync(ModRef modRef, Func<LibraryMod?, LibraryMod?> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        ArgumentNullException.ThrowIfNull(change);

        ModShelfDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                LibraryModRecord? record = await Rows(context, modRef).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                LibraryMod? before = record?.ToMod();
                LibraryMod? after = change(before);
                if (after is not null && after.Mod != modRef)
                {
                    throw new ArgumentException($"The change returned mod {after.Mod.Id} instead of {modRef.Id}.", nameof(change));
                }

                if (after is null)
                {
                    if (record is not null)
                    {
                        context.LibraryMods.Remove(record);
                    }
                }
                else if (record is null)
                {
                    context.LibraryMods.Add(LibraryModRecord.From(after));
                }
                else
                {
                    record.CopyFrom(after);
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new LibraryChange(modRef, before, after);
            }
        }
    }

    private static IQueryable<LibraryModRecord> Rows(ModShelfDbContext context, ModRef modRef)
    {
        string source = modRef.Game.Source.Value;
        string game = modRef.Game.Key;
        string mod = modRef.Id;
        return context.LibraryMods
            .Include(record => record.Files)
            .Include(record => record.Updates)
            .AsSplitQuery()
            .Where(record => record.SourceId == source && record.GameKey == game && record.ModId == mod);
    }
}
