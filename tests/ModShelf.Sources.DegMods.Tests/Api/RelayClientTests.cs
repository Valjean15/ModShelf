using System.Net;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModShelf.Sources.DegMods.Api;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Api;

public sealed class RelayClientTests : IDisposable
{
    private readonly RecordedDegMods _degMods = new();

    [Fact]
    public async Task ReadsAFiltersPagesEachUntilTheOldestOfTheOneBeforeUntilOneBringsNothingNew()
    {
        _degMods.Brs.Cap = 3;
        using ServiceProvider services = _degMods.BuildServices();
        NostrFilter filter = new() { Kinds = [DegModsLink.ModKind], Authors = [RecordedDegMods.MusicAuthor], Limit = 3 };

        IReadOnlyList<NostrEvent> events = await Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [filter], TestContext.Current.CancellationToken);

        Assert.Equal(8, events.Count);
        Assert.Equal(events.Count, events.Select(nostrEvent => nostrEvent.Id).Distinct().Count());
        long?[] untils = [.. _degMods.Brs.Filters.Select(asked => asked["until"]?.GetValue<long>())];
        Assert.Null(untils[0]);
        Assert.True(untils.Length > 3, $"Asked {untils.Length} pages.");
        Assert.Equal([.. untils.Skip(1).OrderDescending()], untils.Skip(1));
    }

    [Fact]
    public async Task AsksAFilterWithoutALimitOnce()
    {
        using ServiceProvider services = _degMods.BuildServices();
        NostrFilter filter = new() { Kinds = [DegModsLink.ModKind], Authors = [RecordedDegMods.MusicAuthor] };

        IReadOnlyList<NostrEvent> events = await Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [filter], TestContext.Current.CancellationToken);

        Assert.Equal(8, events.Count);
        Assert.Single(_degMods.Brs.Filters);
    }

    [Fact]
    public async Task AFilterTheRelayRefusesBringsNothingAndTheNextIsAskedStill()
    {
        using ServiceProvider services = _degMods.BuildServices();
        NostrFilter refused = new() { Kinds = [10000] };
        NostrFilter mods = new() { Kinds = [DegModsLink.ModKind], Authors = [RecordedDegMods.WifiAuthor] };

        IReadOnlyList<NostrEvent> events = await Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [refused, mods], TestContext.Current.CancellationToken);

        Assert.Equal(RecordedDegMods.RejectWifi, Assert.Single(events).D);
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("refused a request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LeavesOutAnEventItsAuthorDidntSignAndSaysSo()
    {
        NostrEvent signed = RecordedDegMods.RecordedEvents("brs.degmods.com").First(nostrEvent => nostrEvent.D == RecordedDegMods.RejectWifi);
        NostrEvent forged = new(signed.Id, signed.PubKey, signed.CreatedAt + 1, signed.Kind, signed.Tags, signed.Content, signed.Sig);
        _degMods.Brs.Remove(signed);
        _degMods.Brs.Add(forged);
        using ServiceProvider services = _degMods.BuildServices();

        IReadOnlyList<NostrEvent> events = await Client(services).ReadAsync(
            _degMods.Address(_degMods.Brs), [new NostrFilter { Authors = [RecordedDegMods.WifiAuthor] }], TestContext.Current.CancellationToken);

        Assert.Empty(events);
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("1 events that aren't what their authors signed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsOnlyItsOwnSubscriptionsAnswersAndNotesNotices()
    {
        NostrEvent other = new TestAuthor(3).Sign(1, 1_700_000_000, [], "not asked for");
        _degMods.Brs.Instead = message =>
        {
            string id = JsonNode.Parse(message)![1]!.GetValue<string>();
            return
            [
                new JsonArray("NOTICE", "slow down").ToJsonString(),
                new JsonArray("EVENT", "someone-else", TestAuthor.Json(other)).ToJsonString(),
                new JsonArray("OK", "x", true, string.Empty).ToJsonString(),
                new JsonArray("EOSE", id).ToJsonString(),
            ];
        };
        using ServiceProvider services = _degMods.BuildServices();

        IReadOnlyList<NostrEvent> events = await Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [new NostrFilter()], TestContext.Current.CancellationToken);

        Assert.Empty(events);
        Assert.Contains(_degMods.Logs.Entries, entry => entry.Contains("said: [\"NOTICE\",\"slow down\"]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[1]")]
    public async Task AMessageThatIsntNostrsIsAnInvalidAnswer(string message)
    {
        _degMods.Brs.Instead = _ => [message];
        using ServiceProvider services = _degMods.BuildServices();

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [new NostrFilter()], TestContext.Current.CancellationToken));

        Assert.Equal(HttpRequestError.InvalidResponse, refused.HttpRequestError);
    }

    [Fact]
    public async Task AMessageLargerThanAnyEventIsAnInvalidAnswer()
    {
        // The relay ends its answer as it should after the message, so that taking it shows as no error, not as a wait.
        _degMods.Brs.Instead = message =>
        {
            JsonArray? request = JsonNode.Parse(message) as JsonArray;
            if (request is not [JsonValue type, JsonValue id, ..] || type.GetValue<string>() != "REQ")
            {
                return [];
            }

            return [new JsonArray("NOTICE", new string('x', RelayClient.MaxMessageBytes)).ToJsonString(), new JsonArray("EOSE", id.GetValue<string>()).ToJsonString()];
        };
        using ServiceProvider services = _degMods.BuildServices();

        HttpRequestException refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [new NostrFilter()], TestContext.Current.CancellationToken));

        Assert.Equal(HttpRequestError.InvalidResponse, refused.HttpRequestError);
        Assert.Contains("larger than ModShelf takes", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamesTheAppToTheRelay()
    {
        using ServiceProvider services = _degMods.BuildServices();

        await Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [new NostrFilter { Authors = [RecordedDegMods.WifiAuthor] }], TestContext.Current.CancellationToken);

        Assert.All(_degMods.Brs.UserAgents, userAgent => Assert.StartsWith("ModShelf/", userAgent, StringComparison.Ordinal));
        Assert.NotEmpty(_degMods.Brs.UserAgents);
    }

    [Fact]
    public async Task ARelayThatDoesntAnswerInTimeIsATimeout()
    {
        TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _degMods.Brs.Instead = _ =>
        {
            asked.TrySetResult();
            return [];
        };
        FakeTimeProvider clock = new(RecordedDegMods.RecordedAt);
        using ServiceProvider services = _degMods.BuildServices(clock: clock);

        Task<IReadOnlyList<NostrEvent>> reading = Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [new NostrFilter()], TestContext.Current.CancellationToken);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(31));

        HttpRequestException late = await Assert.ThrowsAsync<HttpRequestException>(() => reading);
        Assert.IsType<TimeoutException>(late.InnerException);
        Assert.Contains("didn't answer within 30 seconds", late.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARelayThatIsntThereCantBeReached()
    {
        using ServiceProvider services = _degMods.BuildServices(clock: TimeProvider.System);
        Uri nowhere = new(_degMods.Address(_degMods.Brs).AbsoluteUri.Replace("brs.degmods.com", "nowhere.example", StringComparison.Ordinal));

        await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => Client(services).ReadAsync(nowhere, [new NostrFilter()], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StopsWhenTheCallerDoes()
    {
        _degMods.Brs.Instead = _ => [];
        using ServiceProvider services = _degMods.BuildServices();
        using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Client(services).ReadAsync(_degMods.Address(_degMods.Brs), [new NostrFilter()], stop.Token));
    }

    public void Dispose() => _degMods.Dispose();

    private static RelayClient Client(ServiceProvider services) => services.GetRequiredService<RelayClient>();
}
