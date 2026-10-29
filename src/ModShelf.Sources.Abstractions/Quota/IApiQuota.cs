namespace ModShelf.Sources.Abstractions.Quota;

/// <summary>
/// A source whose site allows an account so many requests, and says how many are left.
/// </summary>
public interface IApiQuota : IModSource
{
    /// <summary>
    /// What's left of the account's allowances, as the site's last answer while the app runs said; null before the site
    /// has answered with them.
    /// </summary>
    ApiQuota? Quota { get; }
}
