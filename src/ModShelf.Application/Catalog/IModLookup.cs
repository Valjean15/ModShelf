namespace ModShelf.Application.Catalog;

/// <summary>
/// Looks a mod up on the site a link points to: the game, the mod, its files and its changelog.
/// </summary>
public interface IModLookup
{
    /// <param name="link">A link to a mod or one of its files, on a site whose source has an <c>IModCatalog</c>.</param>
    /// <exception cref="Downloads.UnsupportedLinkException">
    /// No source recognizes the link, or the source that does can't describe its mods.
    /// </exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">
    /// The site couldn't be reached or refused; with 404 when there's no such mod or file.
    /// </exception>
    Task<ModPage> LookUpAsync(Uri link, CancellationToken cancellationToken);
}
