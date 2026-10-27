using ModShelf.Domain.Common;

namespace ModShelf.Domain.Mods;

/// <summary>
/// One downloadable file of a mod, as its source describes it.
/// </summary>
public sealed class ModFile
{
    /// <param name="replacedBy">The newer file of the same mod its site says replaced this one, if any.</param>
    /// <exception cref="ArgumentException">
    /// The name or file name isn't valid, the page isn't a web link, a hash is null, or the file that replaced it is of
    /// another mod, or is this file.
    /// </exception>
    public ModFile(
        ModFileRef reference,
        string name,
        string fileName,
        string? versionLabel,
        ModFileCategory category,
        long? sizeInBytes,
        DateTimeOffset uploadedAt,
        IEnumerable<FileHash> hashes,
        Uri? pageUrl = null,
        ModFileRef? replacedBy = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        FileNames.EnsureSingleSegment(fileName);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown file category.");
        }

        if (sizeInBytes is { } size)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(size, nameof(sizeInBytes));
        }

        if (pageUrl is not null
            && !(pageUrl.IsAbsoluteUri && (pageUrl.Scheme == Uri.UriSchemeHttps || pageUrl.Scheme == Uri.UriSchemeHttp)))
        {
            throw new ArgumentException("A file's page must be an absolute HTTP or HTTPS URL.", nameof(pageUrl));
        }

        ArgumentNullException.ThrowIfNull(hashes);
        FileHash[] hashList = [.. hashes];
        if (Array.Exists(hashList, hash => hash is null))
        {
            throw new ArgumentException("The hashes can't include null.", nameof(hashes));
        }

        if (replacedBy is not null && (replacedBy.Mod != reference.Mod || replacedBy == reference))
        {
            throw new ArgumentException("A file can only be replaced by another file of its mod.", nameof(replacedBy));
        }

        Reference = reference;
        Name = name;
        FileName = fileName;
        VersionLabel = string.IsNullOrWhiteSpace(versionLabel) ? null : versionLabel;
        Category = category;
        SizeInBytes = sizeInBytes;
        UploadedAt = uploadedAt;
        Hashes = Array.AsReadOnly(hashList);
        PageUrl = pageUrl;
        ReplacedBy = replacedBy;
    }

    public ModFileRef Reference { get; }

    /// <summary>
    /// The file's display name on the source, such as "Main File".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The name the file is saved under, such as <c>SkyUI_5_2_SE.7z</c>. It is always a single path segment.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// The author's version label, if any. It is only shown to the user, never parsed or compared.
    /// </summary>
    public string? VersionLabel { get; }

    public ModFileCategory Category { get; }

    /// <summary>
    /// The file's size, when the source reports it.
    /// </summary>
    public long? SizeInBytes { get; }

    public DateTimeOffset UploadedAt { get; }

    /// <summary>
    /// The checksums the source publishes for the file. Empty when it publishes none.
    /// </summary>
    public IReadOnlyList<FileHash> Hashes { get; }

    /// <summary>
    /// The file's page on the source's site, when it has one: a link the source recognizes as this file, so the file
    /// can be downloaded from it.
    /// </summary>
    public Uri? PageUrl { get; }

    /// <summary>
    /// The newer file of the same mod its site says replaced this one, such as the next version in Nexus Mods' update
    /// chains; or null when the site says none did. Following it from file to file leads to the newest version.
    /// </summary>
    public ModFileRef? ReplacedBy { get; }
}
