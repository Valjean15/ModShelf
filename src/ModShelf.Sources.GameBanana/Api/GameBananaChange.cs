using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// One change an update lists.
/// </summary>
internal sealed record GameBananaChange
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
