using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.ModIo.Api;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="ITrendingMods"/> half: the game's mods downloaded most today.
/// </summary>
public sealed partial class ModIoSource : ITrendingMods
{
    /// <returns>The game's 50 mods downloaded most today, the most first, those with explicit content included.</returns>
    /// <exception cref="ArgumentException">The game isn't mod.io's.</exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No key or token is linked, or mod.io refused it.</exception>
    /// <exception cref="HttpRequestException">mod.io couldn't be reached, or refused; with 403 when the game is closed to apps.</exception>
    public async Task<IReadOnlyList<ModInfo>> GetTrendingAsync(GameRef game, CancellationToken cancellationToken)
    {
        long gameId = GameId(game);
        ModIoPage<ModIoMod> found = await ModsPageAsync(gameId, "-downloads_today", 0, MaxPageSize, string.Empty, cancellationToken).ConfigureAwait(false);
        return [.. OfGame(game, gameId, found.Data)];
    }
}
