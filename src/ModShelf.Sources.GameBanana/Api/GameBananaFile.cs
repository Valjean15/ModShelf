using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// One of a mod's files, current or archived.
/// </summary>
internal sealed record GameBananaFile
{
    [JsonPropertyName("_idRow")]
    public long Id { get; init; }

    /// <summary>
    /// Its file name, such as <c>bgme_framework4_0_4.7z</c>.
    /// </summary>
    [JsonPropertyName("_sFile")]
    public string? FileName { get; init; }

    [JsonPropertyName("_nFilesize")]
    public long? Size { get; init; }

    /// <summary>
    /// When it was uploaded, in seconds since 1970.
    /// </summary>
    [JsonPropertyName("_tsDateAdded")]
    public long DateAdded { get; init; }

    /// <summary>
    /// Where it's downloaded from: <c>https://gamebanana.com/dl/&lt;file&gt;</c>, which leads to GameBanana's file server.
    /// </summary>
    [JsonPropertyName("_sDownloadUrl")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("_sMd5Checksum")]
    public string? Md5 { get; init; }

    /// <summary>
    /// Whether its author keeps it apart as an older version.
    /// </summary>
    [JsonPropertyName("_bIsArchived")]
    public bool IsArchived { get; init; }

    /// <summary>
    /// Whether GameBanana read what's in it, as it does an archive; false for a file such as a mod manager's release file.
    /// </summary>
    [JsonPropertyName("_bHasContents")]
    public bool? HasContents { get; init; }

    [JsonPropertyName("_sVersion")]
    public string? Version { get; init; }

    /// <summary>
    /// What its author says of it, such as "Main FeMC mod, DOWNLOAD THIS ONE".
    /// </summary>
    [JsonPropertyName("_sDescription")]
    public string? Description { get; init; }
}
