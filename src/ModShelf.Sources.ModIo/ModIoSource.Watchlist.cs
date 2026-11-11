using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Watchlist;
using ModShelf.Sources.ModIo.Api;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="IWatchlistReader"/> half: the mods the account subscribes to, which ModShelf reads, with an access
/// token, and never changes: games install the mods their players subscribe to.
/// </summary>
public sealed partial class ModIoSource : IWatchlistReader
{
    /// <summary>
    /// The most pages of subscriptions read: 1,000 subscriptions.
    /// </summary>
    internal const int SubscriptionPages = 10;

    /// <returns>Every mod the account subscribes to, of every game, each once.</returns>
    /// <exception cref="AuthenticationFailedException">No token is linked, or mod.io refused it.</exception>
    /// <exception cref="HttpRequestException">
    /// mod.io couldn't be reached, or refused; with 403 when an API key is linked, for which mod.io lists no
    /// subscriptions: the request isn't sent, as mod.io's refusal would have the app ask for the key to be linked again.
    /// </exception>
    public async Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken)
    {
        List<ModIoMod> mods = [];
        for (int page = 0; page < SubscriptionPages; page++)
        {
            string offset = page == 0 ? string.Empty : $"&_offset={Number(page * PageSize)}";
            ModIoPage<ModIoMod> found = await _api.GetAsync<ModIoPage<ModIoMod>>(
                    Api($"me/subscribed?_limit={PageSize}{offset}"),
                    platform: null,
                    tokenOnly: () => new HttpRequestException(
                        "mod.io lists them for an access token only: link one in place of your API key.", null, HttpStatusCode.Forbidden),
                    cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<ModIoMod> data = found.Data ?? [];
            mods.AddRange(data.Where(mod => mod is not null));
            if (data.Count < PageSize || mods.Count >= found.ResultTotal)
            {
                break;
            }
        }

        return
        [
            .. mods
                .Where(mod => mod is { Id: > 0, GameId: > 0 })
                .Select(mod => new ModRef(GameOf(mod.GameId), Number(mod.Id)))
                .Distinct(),
        ];
    }
}
