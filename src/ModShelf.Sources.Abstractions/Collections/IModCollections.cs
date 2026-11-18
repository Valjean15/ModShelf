namespace ModShelf.Sources.Abstractions.Collections;

/// <summary>
/// The site's collections, such as Nexus Mods': sets of its mods' files someone put together, read so that their files
/// can be downloaded into the library. A source implements it only when its site has collections. ModShelf only
/// downloads a collection's files; it never installs anything, nor runs a collection's install steps.
/// </summary>
/// <remarks>
/// It reports a refused account as <see cref="Accounts.AuthenticationFailedException"/> and anything else the site
/// refuses, such as a collection it doesn't have, as <see cref="HttpRequestException"/>.
/// </remarks>
public interface IModCollections : IModSource
{
    /// <summary>
    /// Reads the collection a link is a page of, at the revision the link names, or the latest the site published.
    /// </summary>
    /// <returns>The collection; null, asking nothing of the site, when the link isn't to one of this site's collections.</returns>
    /// <exception cref="ArgumentException">The link is to this site's collections, but can't be read as one.</exception>
    Task<CollectionRevision?> GetCollectionAsync(Uri link, CancellationToken cancellationToken);
}
