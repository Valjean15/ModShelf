namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// The tokens from the OAuth token endpoint, for a new sign-in or a refresh.
/// </summary>
/// <remarks>Secret: never log them.</remarks>
internal sealed record NexusTokens(string AccessToken, string? RefreshToken, int? ExpiresIn)
{
    public override string ToString() => $"NexusTokens {{ ExpiresIn = {ExpiresIn}, HasRefreshToken = {RefreshToken is not null} }}";
}
