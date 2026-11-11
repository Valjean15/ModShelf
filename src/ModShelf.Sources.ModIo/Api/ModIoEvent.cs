using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Something that happened to one of a game's mods, such as its live file changing.
/// </summary>
internal sealed record ModIoEvent
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("mod_id")]
    public long ModId { get; init; }

    [JsonPropertyName("date_added")]
    public long DateAdded { get; init; }

    /// <summary>
    /// Such as <c>MODFILE_CHANGED</c>.
    /// </summary>
    [JsonPropertyName("event_type")]
    public string? EventType { get; init; }
}
