using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A page of a list mod.io gives, such as a game's mods: the records, and where they are in the whole.
/// </summary>
internal sealed record ModIoPage<T>
{
    [JsonPropertyName("data")]
    public IReadOnlyList<T>? Data { get; init; }

    /// <summary>
    /// How many there are in all.
    /// </summary>
    [JsonPropertyName("result_total")]
    public int ResultTotal { get; init; }

    /// <summary>
    /// How many come before this page.
    /// </summary>
    [JsonPropertyName("result_offset")]
    public int ResultOffset { get; init; }
}
