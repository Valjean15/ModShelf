using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// What a page says of the list it's from.
/// </summary>
internal sealed record GameBananaPageMetadata
{
    /// <summary>
    /// How many there are in all.
    /// </summary>
    [JsonPropertyName("_nRecordCount")]
    public int RecordCount { get; init; }

    /// <summary>
    /// How many a page holds, at most.
    /// </summary>
    [JsonPropertyName("_nPerpage")]
    public int PerPage { get; init; }

    /// <summary>
    /// Whether this page is the last.
    /// </summary>
    [JsonPropertyName("_bIsComplete")]
    public bool IsComplete { get; init; }
}
