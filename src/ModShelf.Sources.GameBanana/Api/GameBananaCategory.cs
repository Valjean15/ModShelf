using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// A mod category: one of a game's top categories, or a mod's own. A mod in a list names its top category by its link
/// alone, which ends in its ID.
/// </summary>
internal sealed record GameBananaCategory
{
    [JsonPropertyName("_idRow")]
    public long? Id { get; init; }

    [JsonPropertyName("_sName")]
    public string? Name { get; init; }

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; init; }
}
