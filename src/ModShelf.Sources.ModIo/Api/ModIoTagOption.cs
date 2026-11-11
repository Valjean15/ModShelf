using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A group of tags a game's mods can have, such as "Type".
/// </summary>
internal sealed record ModIoTagOption
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Whether only the game's admins see it.
    /// </summary>
    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }
}
