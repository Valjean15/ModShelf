using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// A mod: its profile, with its files, or an entry of a list of mods, without them.
/// </summary>
internal sealed record GameBananaMod
{
    [JsonPropertyName("_idRow")]
    public long Id { get; init; }

    [JsonPropertyName("_sName")]
    public string? Name { get; init; }

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; init; }

    /// <summary>
    /// A line about it, in the profile only.
    /// </summary>
    [JsonPropertyName("_sDescription")]
    public string? Description { get; init; }

    [JsonPropertyName("_sVersion")]
    public string? Version { get; init; }

    /// <summary>
    /// When it was submitted, in seconds since 1970.
    /// </summary>
    [JsonPropertyName("_tsDateAdded")]
    public long? DateAdded { get; init; }

    /// <summary>
    /// When anything of it last changed, its files included, in seconds since 1970: what GameBanana's latest-modified
    /// order sorts by.
    /// </summary>
    [JsonPropertyName("_tsDateModified")]
    public long? DateModified { get; init; }

    [JsonPropertyName("_nLikeCount")]
    public long? LikeCount { get; init; }

    [JsonPropertyName("_nDownloadCount")]
    public long? DownloadCount { get; init; }

    /// <summary>
    /// How GameBanana shows it at first: <c>show</c>, or <c>warn</c> or <c>hide</c> for a mod rated for adults.
    /// </summary>
    [JsonPropertyName("_sInitialVisibility")]
    public string? InitialVisibility { get; init; }

    [JsonPropertyName("_bIsTrashed")]
    public bool IsTrashed { get; init; }

    [JsonPropertyName("_bIsWithheld")]
    public bool IsWithheld { get; init; }

    [JsonPropertyName("_bIsPrivate")]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("_aSubmitter")]
    public GameBananaMember? Submitter { get; init; }

    [JsonPropertyName("_aGame")]
    public GameBananaGame? Game { get; init; }

    /// <summary>
    /// Its top category, in a list of mods.
    /// </summary>
    [JsonPropertyName("_aRootCategory")]
    public GameBananaCategory? RootCategory { get; init; }

    /// <summary>
    /// Its own category, in the profile.
    /// </summary>
    [JsonPropertyName("_aCategory")]
    public GameBananaCategory? Category { get; init; }

    [JsonPropertyName("_aPreviewMedia")]
    public GameBananaPreviewMedia? PreviewMedia { get; init; }

    /// <summary>
    /// Its current files, in the profile.
    /// </summary>
    [JsonPropertyName("_aFiles")]
    public IReadOnlyList<GameBananaFile>? Files { get; init; }

    /// <summary>
    /// The older versions of its files its author keeps apart, in the profile.
    /// </summary>
    [JsonPropertyName("_aArchivedFiles")]
    public IReadOnlyList<GameBananaFile>? ArchivedFiles { get; init; }
}
