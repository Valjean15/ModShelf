using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// A library in memory. Its changes return only once every watcher has handled them, as the download queue's fake does.
/// </summary>
internal sealed class InMemoryModLibrary : IModLibrary
{
    private readonly Lock _gate = new();
    private readonly List<LibraryMod> _mods = [];
    private readonly ControlledWatchers<LibraryChange> _watchers = new();

    /// <summary>
    /// What was asked of the library, in order, such as "Unwatch 12604" or "Delete 35407".
    /// </summary>
    public List<string> Asked { get; } = [];

    /// <summary>
    /// What changing the library throws, such as an <see cref="IOException"/> for a file that can't be deleted; null for
    /// none.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// Done once a watcher has read everything published and waits for more.
    /// </summary>
    public Task WaitedOn => _watchers.WaitedOn;

    public IReadOnlyList<LibraryMod> Mods
    {
        get
        {
            lock (_gate)
            {
                return [.. _mods];
            }
        }
    }

    /// <summary>
    /// Adds mods that were in the library before anything watched it.
    /// </summary>
    public void Add(params LibraryMod[] mods)
    {
        lock (_gate)
        {
            _mods.AddRange(mods);
        }
    }

    /// <summary>
    /// Changes a mod, as the app does when it records a download or checks for updates, and tells every watcher.
    /// </summary>
    /// <param name="after">The mod as it is now; or null to take <paramref name="mod"/> out of the library.</param>
    public LibraryChange Change(ModRef mod, LibraryMod? after)
    {
        LibraryMod? before;
        lock (_gate)
        {
            int index = _mods.FindIndex(listed => listed.Mod == mod);
            before = index < 0 ? null : _mods[index];
            if (index >= 0)
            {
                _mods.RemoveAt(index);
            }

            if (after is not null)
            {
                _mods.Insert(index < 0 ? _mods.Count : index, after);
            }
        }

        LibraryChange change = new(mod, before, after);
        _watchers.Publish(change);
        return change;
    }

    public Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(Mods);

    public Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult(Mods.FirstOrDefault(mod => mod.Mod == modRef));

    public Task<LibraryMod> WatchAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> UnwatchAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SetWatchedAsync(ModRef modRef, bool watched, CancellationToken cancellationToken)
    {
        Asked.Add($"{(watched ? "Watch" : "Unwatch")} {modRef.Id}");
        if (Refusal is not null)
        {
            throw Refusal;
        }

        LibraryMod mod = Mods.FirstOrDefault(listed => listed.Mod == modRef) ?? throw new KeyNotFoundException($"No mod {modRef.Id}.");
        return Task.FromResult(Change(modRef, watched ? mod.Watched() : mod.Unwatched()));
    }

    public Task<LibraryChange> DeleteFileAsync(ModFileRef file, CancellationToken cancellationToken)
    {
        Asked.Add($"Delete {file.Id}");
        if (Refusal is not null)
        {
            throw Refusal;
        }

        LibraryMod mod = Mods.FirstOrDefault(listed => listed.Mod == file.Mod && listed.Files.Any(kept => kept.File == file))
            ?? throw new KeyNotFoundException($"No file {file.Id}.");
        return Task.FromResult(Change(file.Mod, mod.WithoutFile(file)));
    }

    public IAsyncEnumerable<LibraryChange> ChangesAsync(CancellationToken cancellationToken) => _watchers.Watch(cancellationToken);
}
