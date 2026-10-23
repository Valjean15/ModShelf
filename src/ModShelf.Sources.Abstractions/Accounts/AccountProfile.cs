namespace ModShelf.Sources.Abstractions.Accounts;

/// <summary>
/// Who an account belongs to, as the source reports it. Not secret, so the app may store and show it.
/// </summary>
public sealed record AccountProfile
{
    public AccountProfile(string displayName, Uri? avatarUrl = null, string? tier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (avatarUrl is not null && !WebUrls.IsHttpOrHttps(avatarUrl))
        {
            throw new ArgumentException("The avatar must be an absolute HTTP or HTTPS URL.", nameof(avatarUrl));
        }

        DisplayName = displayName;
        AvatarUrl = avatarUrl;
        Tier = string.IsNullOrWhiteSpace(tier) ? null : tier;
    }

    /// <summary>
    /// The user's name on the site.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// The user's picture, when the site has one.
    /// </summary>
    public Uri? AvatarUrl { get; }

    /// <summary>
    /// The membership level in the site's own words, such as "Premium", when the site has levels.
    /// </summary>
    public string? Tier { get; }
}
