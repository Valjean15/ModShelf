using System.Net;
using System.Text.Json.Nodes;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.GameBanana.Tests.Common;

namespace ModShelf.Sources.GameBanana.Tests;

/// <summary>
/// The mods the linked member subscribes to: read from their subscriptions, 15 a page, mods only, each once.
/// </summary>
public sealed class GameBananaSourceWatchlistTests : IDisposable
{
    private const string SubscriptionsPath = $"/apiv11/Member/{GameBananaHarness.Member}/Subscriptions";

    private readonly GameBananaHarness _linked = new(GameBananaHarness.Member);

    public void Dispose() => _linked.Dispose();

    [Fact]
    public async Task ListsTheModsTheLinkedMemberSubscribesToButNotTheirOtherSubscriptions()
    {
        _linked.Api.Serve("member-subscriptions");

        IReadOnlyList<ModRef> watched = await _linked.Source.GetWatchedAsync(GameBananaHarness.Cancellation);

        Assert.Equal(
            ["16951 432481", "18557 495507", "16951 482718", "5866 168235"],
            watched.Select(mod => $"{mod.Game.Key} {mod.Id}"));
        Assert.All(watched, mod => Assert.Equal(GameBananaHarness.GameBanana, mod.Game.Source));
        Assert.Equal([$"{SubscriptionsPath}?_nPage=1"], _linked.Asked);
    }

    [Fact]
    public async Task WithoutALinkedMemberGameBananaHasNoSuchRouteAndSaysToLinkOne()
    {
        using GameBananaHarness unlinked = new();
        unlinked.Api.Serve("member-subscriptions-unlinked");

        AuthenticationFailedException refused = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => unlinked.Source.GetWatchedAsync(GameBananaHarness.Cancellation));

        Assert.Equal("No GameBanana member is linked, or GameBanana no longer has the one linked.", refused.Message);
        Assert.Equal(["/apiv11/Member/linked/Subscriptions?_nPage=1"], unlinked.Asked);
    }

    [Fact]
    public async Task AMemberGameBananaNoLongerHasIsRefused()
    {
        _linked.Api.Answer(
            SubscriptionsPath,
            answer => answer.WithStatusCode(HttpStatusCode.NotFound).WithBody("""{"_sErrorCode":"NO_SUCH_RECORD","_sErrorMessage":"This Member doesn't exist"}"""));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _linked.Source.GetWatchedAsync(GameBananaHarness.Cancellation));
    }

    [Fact]
    public async Task SaysWhenGameBananaFails()
    {
        _linked.Api.Answer(SubscriptionsPath, answer => answer.WithStatusCode(HttpStatusCode.ServiceUnavailable));

        HttpRequestException failed = await Assert.ThrowsAsync<HttpRequestException>(() => _linked.Source.GetWatchedAsync(GameBananaHarness.Cancellation));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
    }

    [Fact]
    public async Task ReadsEveryPageOfSubscriptions()
    {
        AnswerPages(count: 31, pages: 3);

        IReadOnlyList<ModRef> watched = await _linked.Source.GetWatchedAsync(GameBananaHarness.Cancellation);

        Assert.Equal(["1", "2", "3"], watched.Select(mod => mod.Id).Order(StringComparer.Ordinal));
        Assert.Equal(3, _linked.Api.Requests.Count);
    }

    [Fact]
    public async Task ReadsTheNextPageOfAnUnfinishedListWhateverItsCountSays()
    {
        AnswerPages(count: 1, pages: 2);

        IReadOnlyList<ModRef> watched = await _linked.Source.GetWatchedAsync(GameBananaHarness.Cancellation);

        Assert.Equal(2, watched.Count);
    }

    [Fact]
    public async Task ReadsFortyPagesAtMost()
    {
        AnswerPages(count: 15 * 100, pages: 100);

        IReadOnlyList<ModRef> watched = await _linked.Source.GetWatchedAsync(GameBananaHarness.Cancellation);

        Assert.Equal(40, watched.Count);
        Assert.Equal(40, _linked.Api.Requests.Count);
    }

    [Fact]
    public async Task ListsAModOnceHoweverOftenItComesAndLeavesOutWhatIsntOne()
    {
        JsonArray records =
        [
            Subscribed("Mod", 7, game: 16951),
            Subscribed("Mod", 7, game: 16951),
            Subscribed("Mod", 8, game: null),
            Subscribed("Member", 9, game: null),
            Subscribed("Wip", 10, game: 16951),
            Subscribed("mod", 11, game: 16951),
            new JsonObject { ["_idRow"] = 12 },
        ];
        _linked.Api.Answer(SubscriptionsPath, answer => answer.WithBody(Page(complete: true, count: records.Count, records).ToJsonString()));

        IReadOnlyList<ModRef> watched = await _linked.Source.GetWatchedAsync(GameBananaHarness.Cancellation);

        ModRef only = Assert.Single(watched);
        Assert.Equal(("16951", "7"), (only.Game.Key, only.Id));
    }

    /// <summary>
    /// Answers each page of the subscriptions up to <paramref name="pages"/> with one mod of its own, numbered as the page,
    /// saying there are <paramref name="count"/> in all.
    /// </summary>
    private void AnswerPages(int count, int pages)
    {
        for (int page = 1; page <= pages; page++)
        {
            JsonObject body = Page(complete: page == pages, count, [Subscribed("Mod", page, game: 16951)]);
            _linked.Api.Answer(SubscriptionsPath, "_nPage", page.ToString(System.Globalization.CultureInfo.InvariantCulture), answer => answer.WithBody(body.ToJsonString()));
        }
    }

    private static JsonObject Page(bool complete, int count, JsonArray records) => new()
    {
        ["_aMetadata"] = new JsonObject { ["_nRecordCount"] = count, ["_nPerpage"] = 15, ["_bIsComplete"] = complete },
        ["_aRecords"] = records,
    };

    private static JsonObject Subscribed(string model, long id, long? game) => new()
    {
        ["_idRow"] = id * 100,
        ["_aSubscription"] = new JsonObject
        {
            ["_idRow"] = id,
            ["_sModelName"] = model,
            ["_aGame"] = game is { } gameId ? new JsonObject { ["_idRow"] = gameId } : null,
        },
    };
}
