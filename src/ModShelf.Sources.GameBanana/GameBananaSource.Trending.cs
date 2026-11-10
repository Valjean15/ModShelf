using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Catalog;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="ITrendingMods"/> half: the mods a game's page on GameBanana shows as its top submissions of late.
/// </summary>
public sealed partial class GameBananaSource : ITrendingMods
{
    /// <returns>The game's top mods, as its page lists them, each once, adult ones included.</returns>
    /// <exception cref="ArgumentException">The game isn't GameBanana's.</exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<ModInfo>> GetTrendingAsync(GameRef game, CancellationToken cancellationToken)
    {
        string gameId = GameId(game);
        IReadOnlyList<GameBananaTopMod>? top = await _api.GetAsync<IReadOnlyList<GameBananaTopMod>>($"apiv11/Game/{gameId}/TopSubs", cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. (top ?? [])
                .Where(mod => mod is not null && string.Equals(mod.ModelName, "Mod", StringComparison.Ordinal))
                .DistinctBy(mod => mod.Id)
                .Select(mod => GameBananaMods.ToModInfo(game, mod)),
        ];
    }
}
