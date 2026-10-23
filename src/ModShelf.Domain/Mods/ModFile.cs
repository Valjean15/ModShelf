using ModShelf.Domain.Common;

namespace ModShelf.Domain.Mods;

/// <summary>
/// One downloadable file of a mod, as its source describes it.
/// </summary>
public sealed class ModFile
{
    public ModFile(
        ModFileRef reference,
        string name,
        string fileName,
        string? versionLabel,
        ModFileCategory category,
        long? sizeInBytes,
        DateTimeOffset uploadedAt,
        IEnumerable<FileHash> hashes)
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

        ArgumentNullException.ThrowIfNull(hashes);
        FileHash[] hashList = [.. hashes];
        if (Array.Exists(hashList, hash => hash is null))
        {
            throw new ArgumentException("The hashes can't include null.", nameof(hashes));
        }

        Reference = reference;
        Name = name;
        FileName = fileName;
        VersionLabel = string.IsNullOrWhiteSpace(versionLabel) ? null : versionLabel;
        Category = category;
        SizeInBytes = sizeInBytes;
        UploadedAt = uploadedAt;
        Hashes = Array.AsReadOnly(hashList);
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
}
