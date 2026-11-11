using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// Where a file downloads from, until when.
/// </summary>
internal sealed record ModIoDownload
{
    /// <summary>
    /// On mod.io's API, which sends it on to its file server; with a hash that expires when the game asks downloads to
    /// start from the API.
    /// </summary>
    [JsonPropertyName("binary_url")]
    public string? BinaryUrl { get; init; }

    [JsonPropertyName("date_expires")]
    public long DateExpires { get; init; }
}
