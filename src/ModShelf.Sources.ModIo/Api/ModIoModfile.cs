using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A mod's file: one version of it.
/// </summary>
internal sealed record ModIoModfile
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("mod_id")]
    public long ModId { get; init; }

    [JsonPropertyName("date_added")]
    public long DateAdded { get; init; }

    /// <summary>
    /// 0 not scanned, 1 scanned, 2 scanning, 3 too large to scan, 4 not found, 5 scan failed.
    /// </summary>
    [JsonPropertyName("virus_status")]
    public int VirusStatus { get; init; }

    /// <summary>
    /// 0 nothing found, 1 malicious, 2 possibly harmful, such as a program in it.
    /// </summary>
    [JsonPropertyName("virus_positive")]
    public int VirusPositive { get; init; }

    [JsonPropertyName("filesize")]
    public long Filesize { get; init; }

    [JsonPropertyName("filehash")]
    public ModIoFilehash? Filehash { get; init; }

    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("changelog")]
    public string? Changelog { get; init; }

    [JsonPropertyName("download")]
    public ModIoDownload? Download { get; init; }

    /// <summary>
    /// Whether each platform approved it; none for a game without files per platform.
    /// </summary>
    [JsonPropertyName("platforms")]
    public IReadOnlyList<ModIoFilePlatform>? Platforms { get; init; }
}
