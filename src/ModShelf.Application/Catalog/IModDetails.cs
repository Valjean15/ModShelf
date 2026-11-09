using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Mods' details, such as their pictures and summaries, kept in the library database so they show at once and offline.
/// Saved details are read from the mod's site again once they're older than the settings' <c>DetailsMaxAge</c>.
/// </summary>
/// <remarks>
/// Neither method throws because a site or the database can't be reached: they say what's known, and log the rest.
/// </remarks>
public interface IModDetails
{
    /// <returns>The details saved for the mod, however old, or null when none were: what to show before
    /// <see cref="GetAsync"/> answers.</returns>
    Task<ModInfo?> GetSavedAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <returns>
    /// The details saved for each of the mods that has some, however old, read at once: for a screen showing many mods,
    /// such as a game's in the Library (ADR 0084).
    /// </returns>
    Task<IReadOnlyDictionary<ModRef, ModInfo>> GetSavedAsync(IReadOnlyCollection<ModRef> mods, CancellationToken cancellationToken);

    /// <summary>
    /// The mod's details: the saved ones while they're younger than the settings' <c>DetailsMaxAge</c>, otherwise the
    /// site's, which are saved. Callers asking about a mod while its site is being asked share the answer.
    /// </summary>
    /// <returns>
    /// The details; the saved ones, however old, when the site can't be reached, refuses, or its source isn't registered
    /// or can't describe its mods; null when none were saved either.
    /// </returns>
    Task<ModInfo?> GetAsync(ModRef modRef, CancellationToken cancellationToken);
}
