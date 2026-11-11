using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Downloads;
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

    /// <returns>
    /// Each game DEG Mods shows a mod of, once, named as most of its mods name it, with its cover from DEG Mods' games
    /// database when it has one.
    /// </returns>
    /// <exception cref="HttpRequestException">No relay could be read.</exception>
    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<DegMod> mods, IReadOnlyDictionary<string, Uri> covers) = await ModsAndCoversAsync(cancellationToken).ConfigureAwait(false);
        return [.. mods.GroupBy(mod => ModEvents.GameKey(mod.Game), StringComparer.Ordinal).Select(game => Game(game.Key, game, covers))];
    }

    /// <exception cref="ArgumentException">The game isn't DEG Mods'.</exception>
    /// <exception cref="HttpRequestException">No relay could be read; with 404 when DEG Mods shows no mod of the game.</exception>
    public async Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        string key = GameKey(game);
        (IReadOnlyList<DegMod> mods, IReadOnlyDictionary<string, Uri> covers) = await ModsAndCoversAsync(cancellationToken).ConfigureAwait(false);
        DegMod[] its = [.. mods.Where(mod => ModEvents.GameKey(mod.Game) == key)];
        return its.Length > 0
            ? Game(key, its, covers)
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
    /// Every mod shown, and the covers of their games, read at once.
    /// </summary>
    private async Task<(IReadOnlyList<DegMod> Mods, IReadOnlyDictionary<string, Uri> Covers)> ModsAndCoversAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<DegMod>> mods = _index.AllAsync(cancellationToken);
        IReadOnlyDictionary<string, Uri> covers = await _covers.ReadAsync(GamesOfAsync(mods), cancellationToken).ConfigureAwait(false);
        return (await mods.ConfigureAwait(false), covers);
    }

    private static async Task<IReadOnlySet<string>> GamesOfAsync(Task<IReadOnlyList<DegMod>> mods)
        => (await mods.ConfigureAwait(false)).Select(mod => ModEvents.GameKey(mod.Game)).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The game, named as most of its mods name it, the first in order when as many name it two ways, with its cover.
    /// </summary>
    private GameInfo Game(string key, IEnumerable<DegMod> mods, IReadOnlyDictionary<string, Uri> covers)
    {
        string name = mods
            .GroupBy(mod => mod.Game, StringComparer.Ordinal)
            .OrderByDescending(spelling => spelling.Count())
            .ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
            .First().Key;
        return new GameInfo(new GameRef(DegModsId, key), name, DegModsLink.GamePage(_options.SiteAddress, name), coverUrl: covers.GetValueOrDefault(key));
    }

    private ModInfo Info(DegMod mod, ModRef modRef)
        => new(
            modRef,
            mod.Title,
            mod.Summary,
            Nip19.Npub(mod.Author),
            mod.Downloads.Count > 0 ? mod.Downloads[0].Version : null,
            ModPage(mod),
            PictureOf(mod),
            mod.UpdatedAt,
            isAvailable: true,
            mod.IsAdult,
            thumbnailUrl: PictureOf(mod),
            publishedAt: mod.PublishedAt);

    /// <summary>
    /// The mod's picture: one on DEG Mods' own servers, which give files only to its site, from the first file server
    /// by its SHA-256 instead, as DEG Mods' site asks its file servers too; none on this computer or its network.
    /// </summary>
    private Uri? PictureOf(DegMod mod)
    {
        if (mod.Picture is not { } picture || FileHosts.IsOnThisNetwork(picture))
        {
            return null;
        }

        if (!IsGated(picture) || FileHosts.BlossomHash(picture) is not { } sha256)
        {
            return picture;
        }

        return _options.FileServers.FirstOrDefault(server => !IsGated(server)) is { } fileServer
            ? new Uri(fileServer, sha256 + Path.GetExtension(picture.AbsolutePath))
            : null;
    }
}
