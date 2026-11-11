using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A mod's live file on one platform.
/// </summary>
internal sealed record ModIoModPlatform
{
    /// <summary>
    /// Such as <c>windows</c>.
    /// </summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    [JsonPropertyName("modfile_live")]
    public long ModfileLive { get; init; }
}
