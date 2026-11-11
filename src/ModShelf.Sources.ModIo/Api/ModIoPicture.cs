using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A picture in its sizes: a game's icon or logo, a mod's logo or picture, or a member's avatar; each size only where
/// mod.io makes it.
/// </summary>
internal sealed record ModIoPicture
{
    [JsonPropertyName("original")]
    public string? Original { get; init; }

    [JsonPropertyName("thumb_64x64")]
    public string? Thumb64 { get; init; }

    [JsonPropertyName("thumb_100x100")]
    public string? Thumb100 { get; init; }

    [JsonPropertyName("thumb_128x128")]
    public string? Thumb128 { get; init; }

    [JsonPropertyName("thumb_320x180")]
    public string? Thumb320 { get; init; }

    [JsonPropertyName("thumb_640x360")]
    public string? Thumb640 { get; init; }

    [JsonPropertyName("thumb_1280x720")]
    public string? Thumb1280 { get; init; }
}
