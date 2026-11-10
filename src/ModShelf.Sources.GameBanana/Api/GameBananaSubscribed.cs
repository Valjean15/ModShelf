using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// What a member subscribes to: a mod, or anything else GameBanana has, such as a work in progress or another member.
/// </summary>
internal sealed record GameBananaSubscribed
{
    [JsonPropertyName("_idRow")]
    public long Id { get; init; }

    /// <summary>
    /// What it is, such as <c>Mod</c> or <c>Wip</c>.
    /// </summary>
    [JsonPropertyName("_sModelName")]
    public string? ModelName { get; init; }

    /// <summary>
    /// The game it's of, for a mod.
    /// </summary>
    [JsonPropertyName("_aGame")]
    public GameBananaGame? Game { get; init; }
}
