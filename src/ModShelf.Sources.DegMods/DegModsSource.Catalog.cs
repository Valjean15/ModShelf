using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Links;

namespace ModShelf.Sources.DegMods;

/// <summary>
/// The <see cref="IModCatalog"/> half: the games DEG Mods' mods are of, and a mod and its files read afresh. DEG Mods
/// keeps no changelogs.
/// </summary>
public sealed partial class DegModsSource : IModCatalog
{
    /// <summary>
    /// DEG Mods names its games by their names, lowercase, so any such key could be one; but a key alone of lowercase
    /// letters, digits and hyphens, such as <c>starfield</c>, is taken for another site's (Nexus Mods', or a number for
    /// GameBanana or mod.io), and one with anything else, such as a space, for DEG Mods'. A key with capitals isn't DEG
    /// Mods'.
    /// </summary>
    public bool CouldBeGameKey(string key)
        => !string.IsNullOrWhiteSpace(key)
            && !key.Any(char.IsUpper)
            && key.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-');

    /// <returns>Each game DEG Mods shows a mod of, once, named as most of its mods name it.</returns>
    /// <exception cref="HttpRequestException">No relay could be read.</exception>
    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DegMod> mods = await _index.AllAsync(cancellationToken).ConfigureAwait(false);
        return [.. mods.GroupBy(mod => ModEvents.GameKey(mod.Game), StringComparer.Ordinal).Select(game => Game(game.Key, game))];
    }

    /// <exception cref="ArgumentException">The game isn't DEG Mods'.</exception>
    /// <exception cref="HttpRequestException">No relay could be read; with 404 when DEG Mods shows no mod of the game.</exception>
    public async Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        string key = GameKey(game);
        IReadOnlyList<DegMod> mods = await _index.AllAsync(cancellationToken).ConfigureAwait(false);
        DegMod[] its = [.. mods.Where(mod => ModEvents.GameKey(mod.Game) == key)];
        return its.Length > 0
            ? Game(key, its)
            : throw new HttpRequestException($"DEG Mods shows no mod of the game '{key}'.", null, HttpStatusCode.NotFound);
    }

    /// <exception cref="ArgumentException">The mod isn't DEG Mods'.</exception>
    /// <exception cref="HttpRequestException">
    /// No relay could be read; with 404 when the mod doesn't exist, DEG Mods hides it, or lists it under another game.
    /// </exception>
    public async Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        DegMod mod = await ShownAsync(modRef, cancellationToken).ConfigureAwait(false);
        return Info(mod, modRef);
    }

    /// <returns>The files the mod's newest event lists: the first its main one, the others optional.</returns>
    /// <exception cref="ArgumentException">The mod isn't DEG Mods'.</exception>
    /// <exception cref="HttpRequestException">
    /// No relay could be read; with 404 when the mod doesn't exist, DEG Mods hides it, or lists it under another game.
    /// </exception>
    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        DegMod mod = await ShownAsync(modRef, cancellationToken).ConfigureAwait(false);
        return DegModsFiles.Files(mod, modRef, _options.SiteAddress);
    }

    /// <returns>None: DEG Mods keeps no changelogs.</returns>
    /// <exception cref="ArgumentException">The mod isn't DEG Mods'.</exception>
    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ModKey(modRef);
        return Task.FromResult<IReadOnlyList<ModChangelog>>([]);
    }

    /// <summary>
    /// The game, named as most of its mods name it, the first in order when as many name it two ways.
    /// </summary>
    private GameInfo Game(string key, IEnumerable<DegMod> mods)
    {
        string name = mods
            .GroupBy(mod => mod.Game, StringComparer.Ordinal)
            .OrderByDescending(spelling => spelling.Count())
            .ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
            .First().Key;
        return new GameInfo(new GameRef(DegModsId, key), name, DegModsLink.GamePage(_options.SiteAddress, name));
    }

    private ModInfo Info(DegMod mod, ModRef modRef)
        => new(
            modRef,
            mod.Title,
            mod.Summary,
            Nip19.Npub(mod.Author),
            mod.Downloads.Count > 0 ? mod.Downloads[0].Version : null,
            ModPage(mod),
            mod.Picture,
            mod.UpdatedAt,
            isAvailable: true,
            mod.IsAdult,
            thumbnailUrl: mod.Picture,
            publishedAt: mod.PublishedAt);
}
