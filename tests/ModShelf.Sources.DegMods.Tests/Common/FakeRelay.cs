using System.Collections.Concurrent;
using System.Text.Json.Nodes;

using ModShelf.Sources.DegMods.Api;

namespace ModShelf.Sources.DegMods.Tests.Common;

/// <summary>
/// A Nostr relay (NIP-01) holding the events a test gives it: it answers each REQ with the events that match its filter,
/// the newest first, as many as its limit and the relay's cap allow, then EOSE; a filter for a kind it doesn't serve
/// with CLOSED, as DEG Mods' newer relay does.
/// </summary>
internal sealed class FakeRelay(string host)
{
    private readonly ConcurrentDictionary<string, NostrEvent> _events = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<JsonObject> _filters = new();

    /// <summary>
    /// The relay's host, as DEG Mods' is named: <c>brs.degmods.com</c> or <c>relay.degmods.com</c>.
    /// </summary>
    public string Host { get; } = host;

    /// <summary>
    /// The kinds it refuses a filter for, with CLOSED.
    /// </summary>
    public HashSet<int> RefusedKinds { get; } = [];

    /// <summary>
    /// The most events it sends for one filter.
    /// </summary>
    public int Cap { get; set; } = 500;

    /// <summary>
    /// Whether it leaves out a filter's authors, sending every author's events, as a relay that misbehaves may.
    /// </summary>
    public bool IgnoresAuthors { get; set; }

    /// <summary>
    /// When set, what it answers a message with instead, as a test makes it misbehave; null answers as a relay does.
    /// </summary>
    public Func<string, IReadOnlyList<string>?>? Instead { get; set; }

    /// <summary>
    /// The events it holds.
    /// </summary>
    public IReadOnlyCollection<NostrEvent> Events => [.. _events.Values];

    /// <summary>
    /// The user agent each message came with, oldest first.
    /// </summary>
    public ConcurrentQueue<string> UserAgents { get; } = new();

    /// <summary>
    /// The filters it was asked, oldest first.
    /// </summary>
    public IReadOnlyList<JsonObject> Filters => [.. _filters];

    public void Add(NostrEvent nostrEvent) => _events[nostrEvent.Id] = nostrEvent;

    public void Remove(NostrEvent nostrEvent) => _events.TryRemove(nostrEvent.Id, out _);

    /// <summary>
    /// What it answers a message from a client with.
    /// </summary>
    public IReadOnlyList<string> Answer(string message)
    {
        if (Instead?.Invoke(message) is { } instead)
        {
            return instead;
        }

        JsonArray? request = JsonNode.Parse(message) as JsonArray;
        if (request is not [JsonValue type, JsonValue id, JsonObject filter] || type.GetValue<string>() != "REQ")
        {
            return [];
        }

        _filters.Enqueue(filter);
        string subscription = id.GetValue<string>();
        if (filter["kinds"] is JsonArray kinds && kinds.Any(kind => RefusedKinds.Contains(kind!.GetValue<int>())))
        {
            return [new JsonArray("CLOSED", subscription, "blocked: this relay only serves mod, mod-jam, and moderation-tag events").ToJsonString()];
        }

        int limit = Math.Min(filter["limit"]?.GetValue<int>() ?? Cap, Cap);
        return
        [
            .. _events.Values
                .Where(nostrEvent => Matches(nostrEvent, filter))
                .OrderByDescending(nostrEvent => nostrEvent.CreatedAt)
                .ThenBy(nostrEvent => nostrEvent.Id, StringComparer.Ordinal)
                .Take(limit)
                .Select(nostrEvent => new JsonArray("EVENT", subscription, TestAuthor.Json(nostrEvent)).ToJsonString()),
            new JsonArray("EOSE", subscription).ToJsonString(),
        ];
    }

    private bool Matches(NostrEvent nostrEvent, JsonObject filter)
    {
        foreach ((string name, JsonNode? value) in filter)
        {
            bool matches = name switch
            {
                "ids" => Strings(value).Contains(nostrEvent.Id),
                "kinds" => value!.AsArray().Any(kind => kind!.GetValue<int>() == nostrEvent.Kind),
                "authors" => IgnoresAuthors || Strings(value).Contains(nostrEvent.PubKey),
                "since" => nostrEvent.CreatedAt >= value!.GetValue<long>(),
                "until" => nostrEvent.CreatedAt <= value!.GetValue<long>(),
                "limit" => true,
                ['#', char letter] => nostrEvent.FirstValues(letter.ToString()).Any(Strings(value).Contains),
                _ => false,
            };
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> Strings(JsonNode? value) => [.. value!.AsArray().Select(item => item!.GetValue<string>())];
}
