using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// An update its author posted to a mod: a version and the changes in it.
/// </summary>
internal sealed record GameBananaUpdate
{
    [JsonPropertyName("_sName")]
    public string? Name { get; init; }

    [JsonPropertyName("_sVersion")]
    public string? Version { get; init; }

    [JsonPropertyName("_aChangeLog")]
    public IReadOnlyList<GameBananaChange>? Changes { get; init; }
}
