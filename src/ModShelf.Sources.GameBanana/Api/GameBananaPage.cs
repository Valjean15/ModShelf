using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// A page of a list the API gives, such as a game's mods or a search's results.
/// </summary>
internal sealed record GameBananaPage<T>
{
    [JsonPropertyName("_aMetadata")]
    public GameBananaPageMetadata? Metadata { get; init; }

    [JsonPropertyName("_aRecords")]
    public IReadOnlyList<T>? Records { get; init; }
}
