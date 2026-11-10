using System.Text.Json.Serialization;

namespace ModShelf.Sources.GameBanana.Api;

/// <summary>
/// A member's profile page, as anyone can see it: whose it is, and whether GameBanana shows it.
/// </summary>
internal sealed record GameBananaProfile
{
    [JsonPropertyName("_idRow")]
    public long Id { get; init; }

    /// <summary>
    /// The member's name; none for a banned member, whose page tells only of the ban.
    /// </summary>
    [JsonPropertyName("_sName")]
    public string? Name { get; init; }

    [JsonPropertyName("_sAvatarUrl")]
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// Whether the member keeps their profile to themselves.
    /// </summary>
    [JsonPropertyName("_bIsPrivate")]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("_bIsBanned")]
    public bool IsBanned { get; init; }
}
