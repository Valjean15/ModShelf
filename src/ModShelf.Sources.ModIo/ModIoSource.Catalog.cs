using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;
using ModShelf.Sources.ModIo.Links;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="IModCatalog"/> half: the games open to apps, a game, a mod, its files and what each version changed.
/// A mod's files are its versions, one after the other: the live one, and the earlier ones it replaced.
/// </summary>
public sealed partial class ModIoSource : IModCatalog
{
    /// <summary>
    /// How many records a page of a list holds, for the games and a mod's files: the most mod.io gives.
    /// </summary>
    internal const int PageSize = 100;

    /// <summary>
    /// The most pages of games read: mod.io has about 400 open to apps.
    /// </summary>
    internal const int GamePages = 20;

    /// <summary>
    /// The most pages of a mod's files read.
    /// </summary>
    internal const int FilePages = 5;

    /// <summary>
    /// The flag of a game open to apps such as ModShelf.
    /// </summary>
    private const int OpenToApps = 1;

    /// <summary>
    /// mod.io names its games by number, such as 6715.
    /// </summary>
    public bool CouldBeGameKey(string key) => ModIoLink.IsId(key);

    /// <summary>
    /// Every game open to apps such as ModShelf, by name, a page of 100 at a time, the pages after the first at once.
    /// </summary>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No key or token is linked, or mod.io refused it.</exception>
    /// <exception cref="HttpRequestException">mod.io couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
    {
        ModIoPage<ModIoGame> first = await GamesPageAsync(0, cancellationToken).ConfigureAwait(false);
        int pages = Math.Clamp((first.ResultTotal + PageSize - 1) / PageSize, 1, GamePages);
        ModIoPage<ModIoGame>[] rest = await Task.WhenAll(
                Enumerable.Range(1, pages - 1).Select(page => GamesPageAsync(page * PageSize, cancellationToken)))
            .ConfigureAwait(false);

        GameInfo[] games =
        [
            .. rest.Prepend(first)
                .SelectMany(page => page.Data ?? [])
                .Where(game => game is { Id: > 0 } && (game.ApiAccessOptions & OpenToApps) != 0 && !string.IsNullOrWhiteSpace(game.Name))
                .DistinctBy(game => game.Id)
                .Select(game => ModIoMods.ToGameInfo(GameOf(game.Id), game)),
        ];
        LogGamesRead(_logger, games.Length, pages);
        return games;
    }

    /// <summary>
    /// The game, with its pictures, and its tags as categories.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't mod.io's.</exception>
    /// <exception cref="HttpRequestException">mod.io couldn't be reached, or refused; with 404 when the game doesn't exist.</exception>
    public async Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        long gameId = GameId(game);
        ModIoGame found = await GameAsync(gameId, cancellationToken).ConfigureAwait(false);
        return ModIoMods.ToGameInfo(game, found);
    }

    /// <exception cref="ArgumentException">The mod isn't mod.io's.</exception>
    /// <exception cref="HttpRequestException">
    /// mod.io couldn't be reached, or refused; with 403 when the game is closed to apps, 404 when the mod doesn't exist.
    /// </exception>
    public async Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (long game, long mod) = ModId(modRef);
        return ModIoMods.ToModInfo(modRef.Game, await ModAsync(game, mod, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The mod's versions for Windows: the live one the main file, each earlier one an old version replaced by the next.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't mod.io's.</exception>
    /// <exception cref="HttpRequestException">
    /// mod.io couldn't be reached, or refused; with 403 when the game is closed to apps, 404 when the mod doesn't exist.
    /// </exception>
    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (ModIoMod mod, IReadOnlyList<ModIoModfile> files) = await ModAndFilesAsync(modRef, cancellationToken).ConfigureAwait(false);
        return ModIoFiles.From(modRef, mod, files);
    }

    /// <summary>
    /// What each of the mod's versions for Windows changed, as its author wrote it, the newest first.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't mod.io's.</exception>
    /// <exception cref="HttpRequestException">
    /// mod.io couldn't be reached, or refused; with 403 when the game is closed to apps, 404 when the mod doesn't exist.
    /// </exception>
    public async Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (ModIoMod mod, IReadOnlyList<ModIoModfile> files) = await ModAndFilesAsync(modRef, cancellationToken).ConfigureAwait(false);
        return ModIoFiles.Changelogs(mod, files);
    }

    /// <summary>
    /// The game, and what it lets apps do, kept for downloads.
    /// </summary>
    private async Task<ModIoGame> GameAsync(long gameId, CancellationToken cancellationToken)
    {
        ModIoGame found = await _api.GetAsync<ModIoGame>(GameApi(gameId, $"games/{Number(gameId)}"), cancellationToken).ConfigureAwait(false);
        _access[gameId] = found.ApiAccessOptions;
        return found;
    }

    private Task<ModIoPage<ModIoGame>> GamesPageAsync(int offset, CancellationToken cancellationToken)
        => _api.GetAsync<ModIoPage<ModIoGame>>(
            Api($"games?api_access_options-bitwise-and={OpenToApps}&_sort=name&_limit={PageSize}&_offset={Number(offset)}"),
            cancellationToken);

    /// <summary>
    /// The mod, with its live file for Windows, and every file mod.io lists for it, the newest first, at once.
    /// </summary>
    private async Task<(ModIoMod Mod, IReadOnlyList<ModIoModfile> Files)> ModAndFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (long game, long mod) = ModId(modRef);
        Task<ModIoMod> reading = ModAsync(game, mod, cancellationToken);
        Task<IReadOnlyList<ModIoModfile>> files = FilesAsync(game, mod, cancellationToken);
        await Task.WhenAll(reading, files).ConfigureAwait(false);
        return (await reading.ConfigureAwait(false), await files.ConfigureAwait(false));
    }

    /// <summary>
    /// Every file mod.io lists for the mod, for every platform, the newest first, 100 a page.
    /// </summary>
    private async Task<IReadOnlyList<ModIoModfile>> FilesAsync(long game, long mod, CancellationToken cancellationToken)
    {
        List<ModIoModfile> files = [];
        for (int page = 0; page < FilePages; page++)
        {
            string offset = page == 0 ? string.Empty : $"&_offset={Number(page * PageSize)}";
            ModIoPage<ModIoModfile> found = await _api.GetAsync<ModIoPage<ModIoModfile>>(
                    GameApi(game, $"games/{Number(game)}/mods/{Number(mod)}/files?_sort=-date_added&_limit={PageSize}{offset}"),
                    cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<ModIoModfile> data = found.Data ?? [];
            files.AddRange(data.Where(file => file is not null));
            if (data.Count < PageSize || files.Count >= found.ResultTotal)
            {
                break;
            }
        }

        return files;
    }
}
