using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// One of a game's top submissions of late, as its page shows them: a mod, or something else, such as a sound.
/// </summary>
internal sealed record GameBananaTopMod
{
    [JsonPropertyName("_idRow")]
    public long Id { get; init; }

    /// <summary>
    /// What it is: <c>Mod</c> for a mod.
    /// </summary>
    [JsonPropertyName("_sModelName")]
    public string? ModelName { get; init; }

    [JsonPropertyName("_sName")]
    public string? Name { get; init; }

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; init; }

    [JsonPropertyName("_sImageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("_sThumbnailUrl")]
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// How GameBanana shows it at first: <c>show</c>, or <c>warn</c> or <c>hide</c> for a mod rated for adults.
    /// </summary>
    [JsonPropertyName("_sInitialVisibility")]
    public string? InitialVisibility { get; init; }

    [JsonPropertyName("_aSubmitter")]
    public GameBananaMember? Submitter { get; init; }

    [JsonPropertyName("_nLikeCount")]
    public long? LikeCount { get; init; }
}
