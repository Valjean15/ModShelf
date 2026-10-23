namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// What proves the user's identity to a source: an API key or an OAuth access token, with the refresh token
/// and expiry when the source issues them.
/// </summary>
/// <remarks>
/// Secret. The app keeps credentials only in the operating system's secret store: never in the library
/// database, settings files or the log. <see cref="ToString"/> leaves the tokens out.
/// </remarks>
public sealed record AccountCredentials
{
    public AccountCredentials(string accessToken, string? refreshToken = null, DateTimeOffset? expiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        AccessToken = accessToken;
        RefreshToken = string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// The API key or access token sent with requests.
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    /// The token that gets a new access token, when the source issues one.
    /// </summary>
    public string? RefreshToken { get; }

    /// <summary>
    /// When <see cref="AccessToken"/> stops working, when the source says so.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; }

    public override string ToString()
        => $"AccountCredentials {{ ExpiresAt = {ExpiresAt:O}, HasRefreshToken = {RefreshToken is not null} }}";
}
