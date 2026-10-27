using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Library.Ports;

/// <summary>
/// Stores the library's mods and their files, so they survive restarts. Implemented by Infrastructure.
/// </summary>
public interface ILibraryRepository
{
    /// <returns>Every mod in the library, in the order they were added.</returns>
    Task<IReadOnlyList<LibraryMod>> ListAsync(CancellationToken cancellationToken);

    /// <returns>The mod as stored, or null when it isn't in the library.</returns>
    Task<LibraryMod?> FindAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the mod, passes it to <paramref name="change"/>, and stores what that returns, all in one transaction, so a
    /// change made meanwhile elsewhere is never lost. Null on either side means the mod isn't in the library: returning
    /// null removes it with its files.
    /// </summary>
    /// <param name="change">Works out the mod's new state from its stored one. It must not wait on anything.</param>
    /// <exception cref="ArgumentException"><paramref name="change"/> returned another mod.</exception>
    Task<LibraryChange> UpdateAsync(ModRef modRef, Func<LibraryMod?, LibraryMod?> change, CancellationToken cancellationToken);
}
