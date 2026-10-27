using ModShelf.Domain.Mods;

namespace ModShelf.Domain.Library;

/// <summary>
/// A mod's file, downloaded into the library.
/// </summary>
public sealed class LibraryFile
{
    /// <param name="name">The file's name on its site, such as "Main File".</param>
    /// <param name="versionLabel">The author's version label, if any. Only shown, never parsed or compared.</param>
    /// <param name="uploadedAt">When the file was uploaded to its site.</param>
    /// <param name="relativePath">
    /// Where the file is, relative to the library root, with '/' between folders, such as
    /// <c>nexus/stardewvalley/2400/SMAPI 4.5.2.zip</c>.
    /// </param>
    /// <exception cref="ArgumentException">The name is blank, or the path isn't a relative path inside the library.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The category is unknown, or the size is negative.</exception>
    public LibraryFile(
        ModFileRef file,
        string name,
        string? versionLabel,
        ModFileCategory category,
        DateTimeOffset uploadedAt,
        string relativePath,
        long sizeInBytes,
        DateTimeOffset downloadedAt)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown file category.");
        }

        LibraryPaths.EnsureRelative(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeInBytes);

        File = file;
        Name = name;
        VersionLabel = string.IsNullOrWhiteSpace(versionLabel) ? null : versionLabel;
        Category = category;
        UploadedAt = uploadedAt;
        RelativePath = relativePath;
        SizeInBytes = sizeInBytes;
        DownloadedAt = downloadedAt;
    }

    public ModFileRef File { get; }

    public string Name { get; }

    public string? VersionLabel { get; }

    public ModFileCategory Category { get; }

    public DateTimeOffset UploadedAt { get; }

    /// <summary>
    /// Where the file is, relative to the library root, with '/' between folders.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// The name the file is saved under: the last part of <see cref="RelativePath"/>.
    /// </summary>
    public string FileName => RelativePath[(RelativePath.LastIndexOf(LibraryPaths.Separator) + 1)..];

    public long SizeInBytes { get; }

    public DateTimeOffset DownloadedAt { get; }
}
