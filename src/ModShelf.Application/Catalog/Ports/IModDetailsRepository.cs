using ModShelf.Domain.Mods;

namespace ModShelf.Application.Catalog.Ports;

/// <summary>
/// Keeps mods' details, one set per mod, so they survive restarts. Implemented by Infrastructure.
/// </summary>
/// <remarks>
/// Details are a convenience: a database that can't be read or written for them is logged rather than thrown, and the
/// details are read from the site again.
/// </remarks>
public interface IModDetailsRepository
{
    /// <returns>The mod's saved details, or null when none were saved or the database can't be read.</returns>
    /// <exception cref="InvalidDataException">The saved row holds values no details can have.</exception>
    Task<SavedModDetails?> FindAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <returns>
    /// The saved details of each of the mods that has some; none when the database can't be read. Saved details that hold
    /// values no details can have are left out, and logged.
    /// </returns>
    Task<IReadOnlyDictionary<ModRef, SavedModDetails>> FindAsync(IReadOnlyCollection<ModRef> mods, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the details, replacing the mod's saved ones.
    /// </summary>
    Task SaveAsync(SavedModDetails details, CancellationToken cancellationToken);
}
