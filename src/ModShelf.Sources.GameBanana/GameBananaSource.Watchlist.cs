using System.Globalization;
using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Watchlist;
using ModShelf.Sources.GameBanana.Accounts;
using ModShelf.Sources.GameBanana.Api;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// The <see cref="IWatchlistReader"/> half: the mods the linked member subscribes to, read from their subscriptions,
/// which ModShelf can't change.
/// </summary>
public sealed partial class GameBananaSource : IWatchlistReader
{
    /// <summary>
    /// The most pages of subscriptions read, so a member subscribed to thousands of things doesn't take hundreds of
    /// requests: GameBanana gives 15 a page.
    /// </summary>
    internal const int SubscriptionPages = 40;

    private const string SubscriptionsPath = $"apiv11{GameBananaProfileStrategy.LinkedMember}Subscriptions";

    /// <returns>Every mod the member subscribes to, of every game, each once; works in progress and the rest left out.</returns>
    /// <exception cref="AuthenticationFailedException">No member is linked, or GameBanana no longer has the one linked.</exception>
    /// <exception cref="HttpRequestException">GameBanana couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken)
    {
        GameBananaPage<GameBananaSubscription> first = await SubscriptionsAsync(1, cancellationToken).ConfigureAwait(false);
        int pages = first.Metadata is { IsComplete: false } metadata
            ? Math.Clamp((metadata.RecordCount + Math.Max(metadata.PerPage, 1) - 1) / Math.Max(metadata.PerPage, 1), 2, SubscriptionPages)
            : 1;
        GameBananaPage<GameBananaSubscription>[] rest = await Task.WhenAll(
                Enumerable.Range(2, pages - 1).Select(page => SubscriptionsAsync(page, cancellationToken)))
            .ConfigureAwait(false);

        return
        [
            .. rest.Prepend(first)
                .SelectMany(page => page.Records ?? [])
                .Select(record => record?.Subscription)
                .OfType<GameBananaSubscribed>()
                .Where(subscribed => subscribed is { ModelName: "Mod", Id: > 0, Game.Id: > 0 })
                .Select(subscribed => new ModRef(GameOf(subscribed.Game!.Id), subscribed.Id.ToString(CultureInfo.InvariantCulture)))
                .Distinct(),
        ];
    }

    /// <summary>
    /// A page of the linked member's subscriptions: unlinked, the path names no member, and GameBanana has no such route.
    /// </summary>
    private async Task<GameBananaPage<GameBananaSubscription>> SubscriptionsAsync(int page, CancellationToken cancellationToken)
    {
        try
        {
            return await _api.GetAsync<GameBananaPage<GameBananaSubscription>>(
                    $"{SubscriptionsPath}?_nPage={page.ToString(CultureInfo.InvariantCulture)}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException missing) when (missing.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AuthenticationFailedException("No GameBanana member is linked, or GameBanana no longer has the one linked.", missing);
        }
    }
}
