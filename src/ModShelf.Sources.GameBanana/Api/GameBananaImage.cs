using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// One of a mod's or a game's pictures: a mod's at <see cref="BaseUrl"/> under a file name for each size; a game's, its
/// icon or banner, at <see cref="Url"/>.
/// </summary>
internal sealed record GameBananaImage
{
    /// <summary>
    /// What it is: a mod's <c>screenshot</c>, or a game's <c>icon</c> or <c>banner</c>.
    /// </summary>
    [JsonPropertyName("_sType")]
    public string? Type { get; init; }

    [JsonPropertyName("_sUrl")]
    public string? Url { get; init; }

    [JsonPropertyName("_sBaseUrl")]
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The picture as uploaded.
    /// </summary>
    [JsonPropertyName("_sFile")]
    public string? File { get; init; }

    /// <summary>
    /// The picture 530 pixels wide.
    /// </summary>
    [JsonPropertyName("_sFile530")]
    public string? File530 { get; init; }

    /// <summary>
    /// The picture 220 pixels wide.
    /// </summary>
    [JsonPropertyName("_sFile220")]
    public string? File220 { get; init; }
}
