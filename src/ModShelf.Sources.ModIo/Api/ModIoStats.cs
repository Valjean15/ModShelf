using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// What mod.io counts of a mod.
/// </summary>
internal sealed record ModIoStats
{
    [JsonPropertyName("downloads_total")]
    public long DownloadsTotal { get; init; }

    [JsonPropertyName("ratings_positive")]
    public long RatingsPositive { get; init; }
}
