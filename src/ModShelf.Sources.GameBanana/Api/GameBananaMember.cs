using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// A member of GameBanana: who submitted a mod, or a member a search found.
/// </summary>
internal sealed record GameBananaMember
{
    [JsonPropertyName("_idRow")]
    public long Id { get; init; }

    [JsonPropertyName("_sName")]
    public string? Name { get; init; }
}
