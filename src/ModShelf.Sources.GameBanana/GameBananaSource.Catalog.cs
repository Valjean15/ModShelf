using System.Globalization;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;
using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="IModCatalog"/> half: GameBanana's games, a game, a mod, its files and its updates. GameBanana lists a
/// mod's files side by side, without saying which is the main one or which replaced which (ADR 0087).
/// </summary>
public sealed partial class GameBananaSource : IModCatalog
{
    /// <summary>
    /// How many games a page of the games list holds: the most GameBanana gives.
    /// </summary>
    internal const int GamesPerPage = 50;

    /// <summary>
    /// How many of a mod's updates are read: the latest, as many as a page holds.
    /// </summary>
    internal const int UpdatesRead = 50;

    /// <summary>
    /// GameBanana lists a mod's files side by side: variants, add-ons, and the older versions its author keeps apart.
    /// </summary>
    public bool FilesSideBySide => true;

    /// <summary>
    /// GameBanana names its games by number, such as 16951.
    /// </summary>
    public bool CouldBeGameKey(string key) => GameBananaLink.IsId(key);

    /// <summary>
    /// Every game on GameBanana, in alphabetical order, read a page of 50 at a time, a few pages at once: nearly 100
    /// requests, which the app makes once while it runs.
    /// </summary>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
    {
        GameBananaPage<GameBananaGame> first = await GamesPageAsync(1, cancellationToken).ConfigureAwait(false);
        int pages = Math.Max(1, (int)Math.Ceiling((first.Metadata?.RecordCount ?? 0) / (double)GamesPerPage));
        GameBananaPage<GameBananaGame>[] rest = await Task.WhenAll(
                Enumerable.Range(2, pages - 1).Select(page => GamesPageAsync(page, cancellationToken)))
            .ConfigureAwait(false);

        GameInfo[] games =
        [
            .. new[] { first }.Concat(rest)
                .SelectMany(page => page.Records ?? [])
                .Where(game => game is not null && !string.IsNullOrWhiteSpace(game.Name))
                .DistinctBy(game => game.Id)
                .Select(game => ToGameInfo(GameOf(game.Id), game, categories: [])),
        ];
        LogGamesRead(_logger, games.Length, pages);
        return games;
    }

    /// <summary>
    /// The game, with its pictures and its top mod categories.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't GameBanana's.</exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused; with 404 when the game doesn't exist.</exception>
    public async Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        string gameId = GameId(game);
        GameBananaGame profile = await _api.GetAsync<GameBananaGame>($"apiv11/Game/{gameId}/ProfilePage", cancellationToken).ConfigureAwait(false);
        GameBananaCategory[] categories = [.. (profile.ModRootCategories ?? []).Where(category => category is { Id: not null } && !string.IsNullOrWhiteSpace(category.Name))];
        _categories[gameId] = categories
            .GroupBy(category => category.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id!.Value, StringComparer.OrdinalIgnoreCase);
        return ToGameInfo(game, profile, categories.Select(category => category.Name!));
    }

    /// <exception cref="ArgumentException">The mod isn't GameBanana's.</exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused; with 404 when the mod doesn't exist.</exception>
    public async Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        GameBananaMod profile = await ProfileAsync(ModId(modRef), cancellationToken).ConfigureAwait(false);
        return GameBananaMods.ToModInfo(modRef.Game, profile, profile.Category?.Name);
    }

    /// <summary>
    /// The mod's files: each archive a main file, a file GameBanana couldn't read, such as a mod manager's release file,
    /// miscellaneous, and each older version its author keeps apart an old version, replaced by the newer archive of
    /// the same name, its version left out.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't GameBanana's.</exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused; with 404 when the mod doesn't exist.</exception>
    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        GameBananaMod profile = await ProfileAsync(ModId(modRef), cancellationToken).ConfigureAwait(false);
        return GameBananaFiles.From(modRef, profile);
    }

    /// <summary>
    /// The updates its author posted to the mod, the latest 50, newest first: each version with its changes.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't GameBanana's.</exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused; with 404 when the mod doesn't exist.</exception>
    public async Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        string modId = ModId(modRef);
        GameBananaPage<GameBananaUpdate> updates = await _api.GetAsync<GameBananaPage<GameBananaUpdate>>(
                $"apiv11/Mod/{modId}/Updates?_nPage=1&_nPerpage={UpdatesRead}", cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. (updates.Records ?? [])
                .Where(update => update is not null)
                .Select(update => new ModChangelog(
                    update.Version ?? update.Name ?? string.Empty,
                    (update.Changes ?? []).Select(change => change?.Text ?? string.Empty))),
        ];
    }

    private static GameInfo ToGameInfo(GameRef reference, GameBananaGame game, IEnumerable<string> categories)
    {
        GameBananaImage[] images = [.. (game.PreviewMedia?.Images ?? []).Where(image => image is not null)];
        return new GameInfo(
            reference,
            string.IsNullOrWhiteSpace(game.Name) ? reference.Key : game.Name.Trim(),
            GameBananaMods.WebLink(game.ProfileUrl),
            categories,
            coverUrl: Picture(images, "banner"),
            iconUrl: Picture(images, "icon"));
    }

    /// <summary>
    /// The game's picture of the kind, an icon or a banner; null for none, or the blank one GameBanana gives a game
    /// without.
    /// </summary>
    private static Uri? Picture(GameBananaImage[] images, string type)
        => images.FirstOrDefault(image => string.Equals(image.Type, type, StringComparison.OrdinalIgnoreCase)) is { Url: { } url }
            && !url.EndsWith("/_.gif", StringComparison.OrdinalIgnoreCase)
            ? GameBananaMods.WebLink(url)
            : null;

    private Task<GameBananaPage<GameBananaGame>> GamesPageAsync(int page, CancellationToken cancellationToken)
        => _api.GetAsync<GameBananaPage<GameBananaGame>>(
            $"apiv11/Game/Index?_nPage={page.ToString(CultureInfo.InvariantCulture)}&_nPerpage={GamesPerPage}&_sSort=Generic_Alphabetically",
            cancellationToken);
}
