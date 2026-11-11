using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Whether a platform approved a file.
/// </summary>
internal sealed record ModIoFilePlatform
{
    /// <summary>
    /// Such as <c>windows</c>.
    /// </summary>
    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    /// <summary>
    /// 0 pending, 1 approved, 2 denied, 3 targeted.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }
}
