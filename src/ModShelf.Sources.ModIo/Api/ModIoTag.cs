using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// One of a mod's tags.
/// </summary>
internal sealed record ModIoTag
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
