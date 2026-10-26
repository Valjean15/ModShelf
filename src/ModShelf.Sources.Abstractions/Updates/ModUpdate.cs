using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Sources.Abstractions.Updates;

/// <summary>
/// A mod that changed lately, as an <see cref="IUpdateFeed"/> reports it.
/// </summary>
public sealed record ModUpdate
{
    /// <exception cref="ArgumentException">The page isn't an absolute HTTP or HTTPS URL.</exception>
    public ModUpdate(ModRef mod, DateTimeOffset lastActivityAt, DateTimeOffset? filesUpdatedAt = null, Uri? pageUrl = null)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (pageUrl is not null && !WebUrls.IsHttpOrHttps(pageUrl))
        {
            throw new ArgumentException("A mod's page must be an absolute HTTP or HTTPS URL.", nameof(pageUrl));
        }

        Mod = mod;
        LastActivityAt = lastActivityAt;
        FilesUpdatedAt = filesUpdatedAt;
        PageUrl = pageUrl;
    }

    public ModRef Mod { get; }

    /// <summary>
    /// When anything about the mod last changed: its files, its page, its comments.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; }

    /// <summary>
    /// When a file was last added to the mod or changed, or null when the site doesn't say. This is what an update is.
    /// It can be long before the time the feed was asked about, when only the mod's page changed since.
    /// </summary>
    public DateTimeOffset? FilesUpdatedAt { get; }

    /// <summary>
    /// The mod's page on the site: a link the source's link recognizer takes, or null when the source gives none.
    /// </summary>
    public Uri? PageUrl { get; }
}
