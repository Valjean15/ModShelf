using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A mod: its profile, an entry of a list, or a mod another depends on.
/// </summary>
internal sealed record ModIoMod
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("game_id")]
    public long GameId { get; init; }

    /// <summary>
    /// 1 when mod.io accepted it; 0 not accepted, 3 deleted.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    /// <summary>
    /// 1 when it's public; 0 hidden.
    /// </summary>
    [JsonPropertyName("visible")]
    public int Visible { get; init; }

    [JsonPropertyName("submitted_by")]
    public ModIoUser? SubmittedBy { get; init; }

    [JsonPropertyName("date_added")]
    public long DateAdded { get; init; }

    [JsonPropertyName("date_updated")]
    public long DateUpdated { get; init; }

    [JsonPropertyName("date_live")]
    public long DateLive { get; init; }

    /// <summary>
    /// What mature content it has, as flags: 1 alcohol, 2 drugs, 4 violence, 8 explicit.
    /// </summary>
    [JsonPropertyName("maturity_option")]
    public int MaturityOption { get; init; }

    /// <summary>
    /// What it costs, in the game's tokens; 0 for a free mod.
    /// </summary>
    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("logo")]
    public ModIoPicture? Logo { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The name in its link, such as <c>better-inventory-ui1</c>, which its author can change.
    /// </summary>
    [JsonPropertyName("name_id")]
    public string? NameId { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("profile_url")]
    public string? ProfileUrl { get; init; }

    /// <summary>
    /// Its live file: empty for a game with files per platform, unless one is asked for.
    /// </summary>
    [JsonPropertyName("modfile")]
    public ModIoModfile? Modfile { get; init; }

    /// <summary>
    /// Its live file on each platform.
    /// </summary>
    [JsonPropertyName("platforms")]
    public IReadOnlyList<ModIoModPlatform>? Platforms { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<ModIoTag>? Tags { get; init; }

    [JsonPropertyName("stats")]
    public ModIoStats? Stats { get; init; }
}
