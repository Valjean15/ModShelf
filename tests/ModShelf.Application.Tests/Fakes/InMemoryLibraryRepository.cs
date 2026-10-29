using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// Keeps the library in memory, in the order mods were added. Each update runs under a lock, as the database runs it in
/// a transaction.
/// </summary>
internal sealed class InMemoryLibraryRepository : ILibraryRepository
{
    private readonly Lock _lock = new();
    private readonly List<LibraryMod> _mods = [];

    public IReadOnlyList<LibraryMod> Mods
    {
        get
        {
            lock (_lock)
            {
                return [.. _mods];
            }
        }
    }

    public void Add(LibraryMod mod)
    {
        lock (_lock)
        {
            _mods.Add(mod);
        }
    }

    /// <summary>
    /// What listing the library throws instead, as a database that can't be read does.
    /// </summary>
    public Exception? ListFailure { get; set; }

    public Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken)
        => ListFailure is { } failure ? Task.FromException<IReadOnlyList<LibraryMod>>(failure) : Task.FromResult(Mods);

    public Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult(Mods.FirstOrDefault(mod => mod.Mod == modRef));

    /// <summary>
    /// What the next update throws instead, after running its change, as a database that can't commit would.
    /// </summary>
    public Exception? UpdateFailure { get; set; }

    /// <summary>
    /// Runs after each update that succeeded, with what it did.
    /// </summary>
    public Action<LibraryChange>? AfterUpdate { get; set; }

    public Task<LibraryChange> UpdateAsync(ModRef modRef, Func<LibraryMod?, LibraryMod?> change, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (UpdateFailure is { } failure)
            {
                UpdateFailure = null;
                change(_mods.FirstOrDefault(mod => mod.Mod == modRef));
                return Task.FromException<LibraryChange>(failure);
            }

            int index = _mods.FindIndex(mod => mod.Mod == modRef);
            LibraryMod? before = index < 0 ? null : _mods[index];
            LibraryMod? after = change(before);
            if (after is not null && after.Mod != modRef)
            {
                throw new ArgumentException("The change returned another mod.", nameof(change));
            }

            if (index >= 0)
            {
                _mods.RemoveAt(index);
            }

            if (after is not null)
            {
                _mods.Insert(index < 0 ? _mods.Count : index, after);
            }

            LibraryChange done = new(modRef, before, after);
            AfterUpdate?.Invoke(done);
            return Task.FromResult(done);
        }
    }
}
