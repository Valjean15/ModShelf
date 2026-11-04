using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// A mod's details as the source describes them.
/// </summary>
public sealed record ModInfo
{
    /// <exception cref="ArgumentException">The name is blank, or a link isn't an absolute HTTP or HTTPS URL.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count or the size is negative.</exception>
    public ModInfo(
        ModRef mod,
        string name,
        string? summary = null,
        string? author = null,
        string? version = null,
        Uri? pageUrl = null,
        Uri? pictureUrl = null,
        DateTimeOffset? updatedAt = null,
        bool isAvailable = true,
        bool hasAdultContent = false,
        long? downloads = null,
        long? endorsements = null,
        Uri? thumbnailUrl = null,
        Uri? thumbnailBlurredUrl = null,
        string? category = null,
        long? size = null,
        DateTimeOffset? publishedAt = null)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CatalogUrls.EnsureWebUrl(pageUrl);
        CatalogUrls.EnsureWebUrl(pictureUrl);
        CatalogUrls.EnsureWebUrl(thumbnailUrl);
        CatalogUrls.EnsureWebUrl(thumbnailBlurredUrl);
        if (downloads < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(downloads), downloads, "A count can't be negative.");
        }

        if (endorsements < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endorsements), endorsements, "A count can't be negative.");
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A size can't be negative.");
        }

        Mod = mod;
        Name = name;
        Summary = Blank(summary);
        Author = Blank(author);
        Version = Blank(version);
        PageUrl = pageUrl;
        PictureUrl = pictureUrl;
        UpdatedAt = updatedAt;
        IsAvailable = isAvailable;
        HasAdultContent = hasAdultContent;
        Downloads = downloads;
        Endorsements = endorsements;
        ThumbnailUrl = thumbnailUrl;
        ThumbnailBlurredUrl = thumbnailBlurredUrl;
        Category = Blank(category);
        Size = size;
        PublishedAt = publishedAt;
    }

    public ModRef Mod { get; }

    public string Name { get; }

    /// <summary>
    /// A line or two about the mod, as its author wrote it. The long description isn't carried: each site writes it
    /// in its own markup.
    /// </summary>
    public string? Summary { get; }

    public string? Author { get; }

    /// <summary>
    /// The version the author gives the mod. A label to show, never compared: authors write versions any way they like.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// The mod's page on the site.
    /// </summary>
    public Uri? PageUrl { get; }

    /// <summary>
    /// The mod's main picture.
    /// </summary>
    public Uri? PictureUrl { get; }

    /// <summary>
    /// When the mod last changed, as the site says.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; }

    /// <summary>
    /// False when the mod is hidden, under moderation or removed, so its files can't be downloaded.
    /// </summary>
    public bool IsAvailable { get; }

    public bool HasAdultContent { get; }

    /// <summary>
    /// How many times the mod has been downloaded, as the site counts it, or null when it doesn't say.
    /// </summary>
    public long? Downloads { get; }

    /// <summary>
    /// How many users endorsed, liked or followed the mod, whatever the site counts as approval, or null when it doesn't say.
    /// A site that counts it names it in <see cref="Common.SiteWords.Approvals"/>.
    /// </summary>
    public long? Endorsements { get; }

    /// <summary>
    /// A small version of the picture, for lists, or null when the site gives none: the picture can stand in.
    /// </summary>
    public Uri? ThumbnailUrl { get; }

    /// <summary>
    /// The thumbnail blurred by the site, for adult mods whose pictures the user chose to see blurred, or null when the
    /// site gives none. Shown instead of the thumbnail, so the real picture isn't downloaded.
    /// </summary>
    public Uri? ThumbnailBlurredUrl { get; }

    /// <summary>
    /// The name of the mod's category on its site, such as "Gameplay", or null when it doesn't say.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// The mod's size in bytes, as the site counts it, such as its main file's, or null when it doesn't say.
    /// </summary>
    public long? Size { get; }

    /// <summary>
    /// When the mod was first published on the site, or null when it doesn't say.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
