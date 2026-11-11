using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A file's checksum.
/// </summary>
internal sealed record ModIoFilehash
{
    [JsonPropertyName("md5")]
    public string? Md5 { get; init; }
}
