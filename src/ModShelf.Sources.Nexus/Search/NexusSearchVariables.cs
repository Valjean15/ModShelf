using System.Text.Json.Nodes;

using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Sources.Nexus.Search;

/// <summary>
/// The variables of the <c>SearchMods</c> query for a search. The recording script builds the same ones, so the
/// tests can tell whether a recorded answer is to this search.
/// </summary>
internal static class NexusSearchVariables
{
    private const string MatchingOperator = "MATCHES";
    private const string EqualOperator = "EQUALS";

    /// <param name="game">The game's domain name, already checked.</param>
    public static JsonObject For(string game, SearchQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game);
        ArgumentNullException.ThrowIfNull(query);

        // Every condition must hold. The name is searched by its stemmed words, so "texture" finds "Textures".
        JsonObject filter = new() { ["gameDomainName"] = Condition(game, EqualOperator) };
        if (query.Text is { } text)
        {
            filter["nameStemmed"] = Condition(text, MatchingOperator);
        }

        if (query.Author is { } author)
        {
            filter["author"] = Condition(author, MatchingOperator);
        }

        if (!query.IncludeAdultContent)
        {
            filter["adultContent"] = new JsonArray(new JsonObject { ["value"] = false, ["op"] = EqualOperator });
        }

        return new JsonObject
        {
            ["filter"] = filter,
            ["sort"] = new JsonArray(new JsonObject
            {
                [SortField(query)] = new JsonObject { ["direction"] = query.Direction == SearchDirection.Ascending ? "ASC" : "DESC" },
            }),
            ["offset"] = query.Offset,
            ["count"] = query.Count,
        };
    }

    private static JsonArray Condition(string value, string comparison)
        => new(new JsonObject { ["value"] = value, ["op"] = comparison });

    /// <summary>
    /// Relevance only means something with words to match, so without them the most endorsed come first.
    /// </summary>
    private static string SortField(SearchQuery query) => query.Sort switch
    {
        SearchSort.Relevance when query.Text is not null => "relevance",
        SearchSort.Relevance or SearchSort.Endorsements => "endorsements",
        SearchSort.Downloads => "downloads",
        SearchSort.Updated => "updatedAt",
        SearchSort.Newest => "createdAt",
        _ => throw new ArgumentOutOfRangeException(nameof(query), query.Sort, "Unknown sort."),
    };
}
