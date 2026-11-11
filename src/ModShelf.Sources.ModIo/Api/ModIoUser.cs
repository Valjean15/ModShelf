using System.Text.Json.Serialization;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// A member of mod.io: who submitted a mod, or the account a token belongs to.
/// </summary>
internal sealed record ModIoUser
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("avatar")]
    public ModIoPicture? Avatar { get; init; }
}
