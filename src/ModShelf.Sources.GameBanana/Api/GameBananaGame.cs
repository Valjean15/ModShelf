using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// A game: its profile, an entry of the games list or of a search by name, or the game a mod is of.
/// </summary>
internal sealed record GameBananaGame
{
    [JsonPropertyName("_idRow")]
    public long Id { get; init; }

    [JsonPropertyName("_sName")]
    public string? Name { get; init; }

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; init; }

    /// <summary>
    /// Its icon and banner.
    /// </summary>
    [JsonPropertyName("_aPreviewMedia")]
    public GameBananaPreviewMedia? PreviewMedia { get; init; }

    /// <summary>
    /// Its banner, in a search of games by name.
    /// </summary>
    [JsonPropertyName("_sBannerUrl")]
    public string? BannerUrl { get; init; }

    /// <summary>
    /// Its icon, in a search of games by name.
    /// </summary>
    [JsonPropertyName("_sIconUrl")]
    public string? IconUrl { get; init; }

    /// <summary>
    /// Its top mod categories, in the profile only.
    /// </summary>
    [JsonPropertyName("_aModRootCategories")]
    public IReadOnlyList<GameBananaCategory>? ModRootCategories { get; init; }
}
