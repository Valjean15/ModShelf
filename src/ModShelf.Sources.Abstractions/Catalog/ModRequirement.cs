using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// Something a mod needs, as its author listed it: another mod of the site, or a thing off it.
/// </summary>
public sealed record ModRequirement
{
    /// <param name="mod">The mod needed, when it's one of the site's; null for a thing off the site.</param>
    /// <exception cref="ArgumentException">The name is blank, or the page isn't an absolute HTTP or HTTPS URL.</exception>
    public ModRequirement(string name, ModRef? mod = null, Uri? pageUrl = null, string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CatalogUrls.EnsureWebUrl(pageUrl);

        Name = name.Trim();
        Mod = mod;
        PageUrl = pageUrl;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>
    /// What's needed, as the author named it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The mod needed, when it's one of the site's; null for a thing off the site.
    /// </summary>
    public ModRef? Mod { get; }

    /// <summary>
    /// Where it's found: the mod's page, or the thing's own site.
    /// </summary>
    public Uri? PageUrl { get; }

    /// <summary>
    /// What the author said of it, such as which files need it; null when nothing.
    /// </summary>
    public string? Notes { get; }
}
