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
    /// What was asked of the library, in order, such as "Unwatch 12604", "Delete 35407" or "Forget 12604".
    /// </summary>
    public List<string> Asked { get; } = [];

    /// <summary>
    /// What changing the library throws, such as an <see cref="IOException"/> for a file that can't be deleted; null for
    /// none.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// What undoing a change throws, such as an <see cref="InvalidOperationException"/> for a mod back in the library
    /// since; null for none.
    /// </summary>
    public Exception? RestoreRefusal { get; set; }

    /// <summary>
    /// Holds each undoing until set; null to undo at once.
    /// </summary>
    public TaskCompletionSource? RestoreHeld { get; set; }

    /// <summary>
    /// The names mods' sites give them, by mod ID, for watching a mod from its site.
    /// </summary>
    public Dictionary<string, string> SiteNames { get; } = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Holds each listing until set, as a slow database does; null to list at once.
    /// </summary>
    public TaskCompletionSource? ListingHeld { get; init; }

    /// <summary>
    /// What listing the library throws, such as an <see cref="InvalidDataException"/>; null for nothing.
    /// </summary>
    public Exception? ListRefusal { get; set; }

    public async Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken)
    {
        if (ListingHeld is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        return ListRefusal is null ? Mods : throw ListRefusal;
    }

    public Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken)
        => Task.FromResult(Mods.FirstOrDefault(mod => mod.Mod == modRef));

    public Task<LibraryMod> WatchAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>
    /// Watches the mod, adding it under the name <see cref="SiteNames"/> gives it, as its site would.
    /// </summary>
    public Task<LibraryMod> WatchAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        Asked.Add($"Watch from the site {modRef.Id}");
        if (Refusal is not null)
        {
            throw Refusal;
        }

        LibraryMod? listed = Mods.FirstOrDefault(mod => mod.Mod == modRef);
        string name = SiteNames.GetValueOrDefault(modRef.Id, $"Mod {modRef.Id}");
        LibraryMod watched = listed?.Watched() ?? LibraryMod.Watch(modRef, name, author: null, pageUrl: null, TestData.Now);
        return Task.FromResult(Change(modRef, watched).After!);
    }

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

    public Task<LibraryChange> ForgetAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> ForgetAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        Asked.Add($"Forget {modRef.Id}");
        if (Refusal is not null)
        {
            throw Refusal;
        }

        _ = Mods.FirstOrDefault(listed => listed.Mod == modRef) ?? throw new KeyNotFoundException($"No mod {modRef.Id}.");
        return Task.FromResult(Change(modRef, null));
    }

    public Task<LibraryChange> SetPinnedAsync(ModRef modRef, bool pinned, CancellationToken cancellationToken)
    {
        Asked.Add($"{(pinned ? "Pin" : "Unpin")} {modRef.Id}");
        return Task.FromResult(Change(modRef, Find(modRef).WithPinned(pinned)));
    }

    public Task<LibraryChange> SetPinnedAsync(Uri link, bool pinned, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SkipUpdateAsync(ModFileRef update, bool skip, CancellationToken cancellationToken)
    {
        Asked.Add($"{(skip ? "Skip" : "Offer")} {update.Id}");
        return Task.FromResult(Change(update.Mod, Find(update.Mod).WithUpdateSkipped(update, skip)));
    }

    public Task<LibraryChange> SkipUpdateAsync(Uri link, bool skip, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SetNoteAsync(ModRef modRef, string? note, CancellationToken cancellationToken)
    {
        Asked.Add($"Note {modRef.Id}: {note}");
        return Task.FromResult(Change(modRef, Find(modRef).WithNote(note)));
    }

    public Task<LibraryChange> SetNoteAsync(Uri link, string? note, CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>
    /// Puts the mod back as it was before the change.
    /// </summary>
    public async Task<LibraryChange> RestoreAsync(LibraryChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        Asked.Add($"Restore {change.Mod.Id}");
        if (RestoreHeld is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        return RestoreRefusal is { } refusal ? throw refusal : Change(change.Mod, change.Before);
    }

    public IAsyncEnumerable<LibraryChange> ChangesAsync(CancellationToken cancellationToken) => _watchers.Watch(cancellationToken);

    /// <summary>
    /// The mod, unless the library refuses the change or doesn't have it.
    /// </summary>
    private LibraryMod Find(ModRef modRef)
    {
        if (Refusal is not null)
        {
            throw Refusal;
        }

        return Mods.FirstOrDefault(listed => listed.Mod == modRef) ?? throw new KeyNotFoundException($"No mod {modRef.Id}.");
    }
}
