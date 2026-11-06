using ModShelf.Application.Library;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A library that only lists its mods, or fails with <see cref="Failure"/>.
/// </summary>
internal sealed class ListedLibrary : IModLibrary
{
    public List<LibraryMod> Mods { get; } = [];

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken)
        => Failure is null ? Task.FromResult<IReadOnlyList<LibraryMod>>([.. Mods]) : Task.FromException<IReadOnlyList<LibraryMod>>(Failure);

    public Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryMod> WatchAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryMod> WatchAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> UnwatchAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SetWatchedAsync(ModRef modRef, bool watched, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> DeleteFileAsync(ModFileRef file, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> ForgetAsync(Uri link, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> ForgetAsync(ModRef modRef, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SetPinnedAsync(ModRef modRef, bool pinned, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SetPinnedAsync(Uri link, bool pinned, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SkipUpdateAsync(ModFileRef update, bool skip, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SkipUpdateAsync(Uri link, bool skip, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SetNoteAsync(ModRef modRef, string? note, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> SetNoteAsync(Uri link, string? note, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<LibraryChange> RestoreAsync(LibraryChange change, CancellationToken cancellationToken) => throw new NotSupportedException();

    public IAsyncEnumerable<LibraryChange> ChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
