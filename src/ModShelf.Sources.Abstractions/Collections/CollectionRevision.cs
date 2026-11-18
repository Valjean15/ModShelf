using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Sources.Abstractions.Collections;

/// <summary>
/// A revision of one of the site's collections: the files it takes from the site's mods, and what it needs from
/// elsewhere.
/// </summary>
public sealed record CollectionRevision
{
    /// <param name="revision">The revision read, as the site numbers them; null when it doesn't.</param>
    /// <param name="filesUnavailable">How many of the files the collection lists the site no longer offers.</param>
    /// <exception cref="ArgumentException">
    /// The name is blank, the page isn't an absolute HTTP or HTTPS URL, or the count of files no longer offered is
    /// negative.
    /// </exception>
    public CollectionRevision(
        string name,
        GameRef game,
        Uri pageUrl,
        int? revision,
        IReadOnlyList<CollectionFile> files,
        IReadOnlyList<CollectionResource> elsewhere,
        int filesUnavailable = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(pageUrl);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(elsewhere);
        ArgumentOutOfRangeException.ThrowIfNegative(filesUnavailable);
        CatalogUrls.EnsureWebUrl(pageUrl);

        Name = name.Trim();
        Game = game;
        PageUrl = pageUrl;
        Revision = revision;
        Files = files;
        Elsewhere = elsewhere;
        FilesUnavailable = filesUnavailable;
    }

    /// <summary>
    /// The collection's name, as its curator called it.
    /// </summary>
    public string Name { get; }

    public GameRef Game { get; }

    /// <summary>
    /// The collection's page on the site.
    /// </summary>
    public Uri PageUrl { get; }

    /// <summary>
    /// The revision read, as the site numbers them; null when it doesn't.
    /// </summary>
    public int? Revision { get; }

    /// <summary>
    /// The files the collection takes from the site's mods, in the order the site lists them, each once.
    /// </summary>
    public IReadOnlyList<CollectionFile> Files { get; }

    /// <summary>
    /// What the collection needs from elsewhere, such as a script extender: never downloaded, only named.
    /// </summary>
    public IReadOnlyList<CollectionResource> Elsewhere { get; }

    /// <summary>
    /// How many of the files the collection lists the site no longer offers, so can't be named.
    /// </summary>
    public int FilesUnavailable { get; }
}
