using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A game: its profile, or an entry of the games list.
/// </summary>
internal sealed record ModIoGame
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The name in its links, such as <c>baldursgate3</c> in <c>mod.io/g/baldursgate3</c>.
    /// </summary>
    [JsonPropertyName("name_id")]
    public string? NameId { get; init; }

    [JsonPropertyName("profile_url")]
    public string? ProfileUrl { get; init; }

    /// <summary>
    /// What it lets apps do, as flags: 1 use its API at all, 2 download directly, 4 download only with a token, 8 paid
    /// mods only when owned.
    /// </summary>
    [JsonPropertyName("api_access_options")]
    public int ApiAccessOptions { get; init; }

    [JsonPropertyName("icon")]
    public ModIoPicture? Icon { get; init; }

    [JsonPropertyName("logo")]
    public ModIoPicture? Logo { get; init; }

    /// <summary>
    /// The groups of tags its mods can have, which ModShelf takes for its categories.
    /// </summary>
    [JsonPropertyName("tag_options")]
    public IReadOnlyList<ModIoTagOption>? TagOptions { get; init; }
}
