using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// A mod's details as the source describes them.
/// </summary>
public sealed record ModInfo
{
    /// <exception cref="ArgumentException">The name is blank, or a link isn't an absolute HTTP or HTTPS URL.</exception>
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
        bool hasAdultContent = false)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CatalogUrls.EnsureWebUrl(pageUrl);
        CatalogUrls.EnsureWebUrl(pictureUrl);

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

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
