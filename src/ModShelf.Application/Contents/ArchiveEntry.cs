namespace ModShelf.Application.Contents;

/// <summary>
/// A folder or a file in an archive, as the archive lists it. Its path is only ever shown: ModShelf never extracts an
/// archive, so it's never a path on disk (ADR 0106).
/// </summary>
/// <param name="Path">Where it is in the archive, with '/' between folders, such as <c>Data/Textures/lantern.dds</c>.</param>
/// <param name="SizeInBytes">Its size once unpacked; 0 for a folder.</param>
public sealed record ArchiveEntry(string Path, long SizeInBytes, bool IsFolder)
{
    // The kinds of file shown as text: readmes, notes, and the settings files mods carry.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".nfo", ".log", ".ini", ".cfg", ".conf", ".toml", ".json", ".jsonc", ".xml", ".yaml", ".yml", ".csv", ".lua",
    };

    /// <summary>
    /// Its name: the last part of <see cref="Path"/>.
    /// </summary>
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The folder it's in, such as <c>Data/Textures</c>; empty at the archive's top.
    /// </summary>
    public string Folder => Path.LastIndexOf('/') is var separator and >= 0 ? Path[..separator] : string.Empty;

    /// <summary>
    /// How many folders deep it is: 0 at the archive's top.
    /// </summary>
    public int Depth => Path.Count(character => character == '/');

    /// <summary>
    /// Whether it's a file that can be shown as text, by its kind, such as a readme.
    /// </summary>
    public bool IsText => !IsFolder && TextExtensions.Contains(System.IO.Path.GetExtension(Name));
}
