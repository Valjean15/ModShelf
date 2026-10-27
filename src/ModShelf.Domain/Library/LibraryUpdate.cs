using ModShelf.Domain.Common;
using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Library;

/// <summary>
/// An update a check found for a library mod: a newer file of the mod on its site, which the library doesn't have.
/// </summary>
public sealed class LibraryUpdate
{
    /// <param name="name">The file's name on its site, such as "Main File".</param>
    /// <param name="fileName">The name its site saves it under. A single path segment.</param>
    /// <param name="versionLabel">The author's version label, if any. Only shown, never parsed or compared.</param>
    /// <param name="replaces">The library file it's an update of; or null for a mod that's only watched.</param>
    /// <param name="foundAt">When a check first found it.</param>
    /// <param name="pageUrl">The file's page on its site, to download it from, if it has one.</param>
    /// <exception cref="ArgumentException">
    /// The name or file name isn't valid, the page isn't a web link, or the file it replaces is of another mod, or is
    /// this file.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The category is unknown.</exception>
    public LibraryUpdate(
        ModFileRef file,
        string name,
        string fileName,
        string? versionLabel,
        ModFileCategory category,
        DateTimeOffset uploadedAt,
        ModFileRef? replaces,
        DateTimeOffset foundAt,
        Uri? pageUrl = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        FileNames.EnsureSingleSegment(fileName);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown file category.");
        }

        if (replaces is not null && (replaces.Mod != file.Mod || replaces == file))
        {
            throw new ArgumentException("An update replaces another file of its mod.", nameof(replaces));
        }

        if (pageUrl is not null
            && !(pageUrl.IsAbsoluteUri && (pageUrl.Scheme == Uri.UriSchemeHttps || pageUrl.Scheme == Uri.UriSchemeHttp)))
        {
            throw new ArgumentException("An update's page must be an absolute HTTP or HTTPS URL.", nameof(pageUrl));
        }

        File = file;
        Name = name;
        FileName = fileName;
        VersionLabel = string.IsNullOrWhiteSpace(versionLabel) ? null : versionLabel;
        Category = category;
        UploadedAt = uploadedAt;
        Replaces = replaces;
        FoundAt = foundAt;
        PageUrl = pageUrl;
    }

    /// <summary>
    /// The newer file.
    /// </summary>
    public ModFileRef File { get; }

    public string Name { get; }

    public string FileName { get; }

    public string? VersionLabel { get; }

    public ModFileCategory Category { get; }

    public DateTimeOffset UploadedAt { get; }

    /// <summary>
    /// The library file this is an update of; or null for an update of a mod that's only watched.
    /// </summary>
    public ModFileRef? Replaces { get; }

    /// <summary>
    /// When a check first found it.
    /// </summary>
    public DateTimeOffset FoundAt { get; }

    /// <summary>
    /// The file's page on its site, a link the source recognizes as this file, so it can be downloaded from it.
    /// </summary>
    public Uri? PageUrl { get; }

    /// <summary>
    /// The update, as first found at <paramref name="foundAt"/>.
    /// </summary>
    public LibraryUpdate FoundSince(DateTimeOffset foundAt)
        => new(File, Name, FileName, VersionLabel, Category, UploadedAt, Replaces, foundAt, PageUrl);
}
