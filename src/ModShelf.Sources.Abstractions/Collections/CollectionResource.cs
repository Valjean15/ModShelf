using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Collections;

/// <summary>
/// Something a collection needs from outside the site, such as a script extender or a file on another site. ModShelf
/// names it and its page; the user gets it.
/// </summary>
public sealed record CollectionResource
{
    /// <exception cref="ArgumentException">The name is blank, or the page isn't an absolute HTTP or HTTPS URL.</exception>
    public CollectionResource(string name, Uri? pageUrl = null, bool optional = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CatalogUrls.EnsureWebUrl(pageUrl);

        Name = name.Trim();
        PageUrl = pageUrl;
        Optional = optional;
    }

    public string Name { get; }

    /// <summary>
    /// Where it's found; null when the collection doesn't say.
    /// </summary>
    public Uri? PageUrl { get; }

    /// <summary>
    /// Whether the collection marks it optional.
    /// </summary>
    public bool Optional { get; }
}
