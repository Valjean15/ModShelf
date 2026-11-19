using ModShelf.Domain.Mods;

namespace ModShelf.Application.Cleanup;

/// <summary>
/// What could go from the library's folders to free up space, and what's kept (ADR 0104).
/// </summary>
/// <param name="Game">The game it's for; null for every game.</param>
/// <param name="Entries">
/// The files, by group in <see cref="CleanupGroup"/>'s order, then by game, mod name and path.
/// </param>
/// <param name="Asked">How many mods' sites were asked which files they offer.</param>
public sealed record CleanupPlan(GameRef? Game, IReadOnlyList<CleanupEntry> Entries, int Asked)
{
    /// <summary>
    /// The bytes the files that can go take up.
    /// </summary>
    public long CanFree => Entries.Where(entry => entry.CanGo).Sum(entry => entry.SizeInBytes);

    /// <summary>
    /// The files of <paramref name="group"/> that can go.
    /// </summary>
    public IReadOnlyList<CleanupEntry> CanGoIn(CleanupGroup group) => [.. Entries.Where(entry => entry.Group == group && entry.CanGo)];
}
