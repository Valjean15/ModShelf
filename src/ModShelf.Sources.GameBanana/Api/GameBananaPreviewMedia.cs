using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// A mod's or a game's pictures.
/// </summary>
internal sealed record GameBananaPreviewMedia
{
    [JsonPropertyName("_aImages")]
    public IReadOnlyList<GameBananaImage>? Images { get; init; }
}
