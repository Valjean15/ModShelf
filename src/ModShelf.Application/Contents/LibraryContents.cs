using System.Text;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Contents.Ports;
using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Contents;

/// <summary>
/// Lists the library's archives and reads their text files through the <see cref="IArchiveReader"/>, which reads them
/// where they are (ADR 0106).
/// </summary>
internal sealed partial class LibraryContents(
    IModLibrary library,
    ISettingsService settings,
    ILibraryStorage storage,
    IArchiveReader reader,
    ILogger<LibraryContents> logger) : ILibraryContents
{
    // How far into a file a NUL byte says it isn't text.
    private const int SniffedBytes = 8000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public bool CanList(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return reader.FormatOf(fileName) is not null;
    }

    public async Task<LibraryMod> FindModAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
        return LibraryLinks.ModAt(mods, link) is { } found
            ? mods.First(mod => mod.Mod == found)
            : throw new KeyNotFoundException($"The library has no mod at {link}.");
    }

    public async Task<ArchiveContents> ListAsync(ModFileRef file, CancellationToken cancellationToken)
    {
        (LibraryFile found, string path, string format) = await LocateAsync(file, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ArchiveEntry> listed;
        try
        {
            listed = await reader.ListAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (ArchiveUnreadableException exception)
        {
            LogUnreadable(logger, exception, found.FileName, exception.Trouble);
            throw;
        }

        ArchiveEntry[] entries = Whole(listed);
        int files = entries.Count(entry => !entry.IsFolder);
        long size = entries.Sum(entry => entry.SizeInBytes);
        LogListed(logger, found.FileName, files, entries.Length - files);
        return new ArchiveContents(file, found.FileName, format, entries, files, size, ReadmeOf(entries));
    }

    public async Task<ArchiveText> ReadTextAsync(ModFileRef file, string entry, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        (LibraryFile found, string path, _) = await LocateAsync(file, cancellationToken).ConfigureAwait(false);
        string wanted = Normalized(entry);
        ArchiveBytes bytes;
        try
        {
            bytes = await reader.ReadAsync(path, wanted, ILibraryContents.MaxTextBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (ArchiveUnreadableException exception)
        {
            LogUnreadable(logger, exception, found.FileName, exception.Trouble);
            throw;
        }

        bool cut = bytes.SizeInBytes > bytes.Bytes.Length;
        string text = Decode(bytes.Bytes.Span, cut)
            ?? throw new ArchiveUnreadableException(ArchiveTrouble.NotText, $"{wanted} in {found.FileName} isn't text.");
        return new ArchiveText(wanted, text, bytes.SizeInBytes, cut);
    }

    /// <summary>
    /// Text in UTF-8, or in UTF-16 when it starts with UTF-16's byte order mark, or else in Latin-1; a character cut by
    /// the end of what was read is left out. Null for bytes that aren't text: with a NUL byte near their start.
    /// </summary>
    internal static string? Decode(ReadOnlySpan<byte> bytes, bool cut)
    {
        if (bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..])
        {
            Encoding utf16 = bytes[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode;
            ReadOnlySpan<byte> body = bytes[2..];
            return utf16.GetString(body[..(body.Length & ~1)]);
        }

        if (bytes[..Math.Min(bytes.Length, SniffedBytes)].Contains((byte)0))
        {
            return null;
        }

        ReadOnlySpan<byte> utf8 = bytes is [0xEF, 0xBB, 0xBF, ..] ? bytes[3..] : bytes;
        try
        {
            Decoder decoder = StrictUtf8.GetDecoder();
            char[] characters = new char[StrictUtf8.GetMaxCharCount(utf8.Length)];

            // Not flushed when cut short: the bytes of a character the cut splits are held back, not refused.
            int count = decoder.GetChars(utf8, characters, flush: !cut);
            return new string(characters, 0, count);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>
    /// An entry's path as the listing gives it: '/' between folders, without an empty, a <c>.</c> or a leading part.
    /// </summary>
    internal static string Normalized(string path)
        => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(part => part != "."));

    /// <summary>
    /// The entries with their paths normalized, every folder their paths imply listed once, ordered by path, each folder
    /// before what's in it.
    /// </summary>
    private static ArchiveEntry[] Whole(IReadOnlyList<ArchiveEntry> listed)
    {
        Dictionary<string, ArchiveEntry> folders = new(StringComparer.Ordinal);
        List<ArchiveEntry> files = [];
        foreach (ArchiveEntry entry in listed)
        {
            string path = Normalized(entry.Path);
            if (path.Length == 0)
            {
                continue;
            }

            if (entry.IsFolder)
            {
                folders.TryAdd(path, new ArchiveEntry(path, 0, IsFolder: true));
            }
            else
            {
                files.Add(entry with { Path = path });
            }

            for (int separator = path.LastIndexOf('/'); separator > 0; separator = path.LastIndexOf('/', separator - 1))
            {
                string folder = path[..separator];
                if (!folders.TryAdd(folder, new ArchiveEntry(folder, 0, IsFolder: true)))
                {
                    break;
                }
            }
        }

        return [.. folders.Values.Concat(files).Order(EntryOrder.Instance)];
    }

    /// <summary>
    /// The readme nearest the archive's top: a text file whose name starts with "readme", then any other whose name
    /// says it's one, such as "Mod - Read Me.txt".
    /// </summary>
    private static string? ReadmeOf(IEnumerable<ArchiveEntry> entries)
        => entries
            .Where(entry => entry.IsText && IsReadme(entry.Name))
            .OrderBy(entry => entry.Depth)
            .ThenBy(entry => entry.Name.StartsWith("readme", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Path;

    private static bool IsReadme(string name)
    {
        string bare = name.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return bare.Contains("readme", StringComparison.OrdinalIgnoreCase) || bare.Contains("liesmich", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The library's file, its path on disk, and its kind of archive.
    /// </summary>
    private async Task<(LibraryFile File, string Path, string Format)> LocateAsync(ModFileRef file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        LibraryFile found = (await library.FindAsync(file.Mod, cancellationToken).ConfigureAwait(false))?.Files.FirstOrDefault(own => own.File == file)
            ?? throw new KeyNotFoundException($"The library doesn't have the file {file}.");
        string format = reader.FormatOf(found.FileName)
            ?? throw new ArchiveUnreadableException(
                ArchiveTrouble.NotAnArchive,
                $"{found.FileName} isn't an archive ModShelf can look into: it reads .zip, .7z and .rar files.");
        string path = (await settings.GetLibraryRootsAsync(cancellationToken).ConfigureAwait(false)).Resolve(found);
        return storage.FileExists(path)
            ? (found, path, format)
            : throw new FileNotFoundException($"{found.FileName} isn't in the library's folder any more. A rescan finds it if it was moved.", path);
    }

    /// <summary>
    /// By path, part by part, folders first among what's in a folder, in any case first, then exactly.
    /// </summary>
    private sealed class EntryOrder : IComparer<ArchiveEntry>
    {
        public static readonly EntryOrder Instance = new();

        public int Compare(ArchiveEntry? x, ArchiveEntry? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null || y is null)
            {
                return x is null ? -1 : 1;
            }

            string[] left = x.Path.Split('/');
            string[] right = y.Path.Split('/');
            for (int index = 0; index < Math.Min(left.Length, right.Length); index++)
            {
                bool leftFolder = index < left.Length - 1 || x.IsFolder;
                bool rightFolder = index < right.Length - 1 || y.IsFolder;
                if (leftFolder != rightFolder)
                {
                    return leftFolder ? -1 : 1;
                }

                int byName = StringComparer.OrdinalIgnoreCase.Compare(left[index], right[index]);
                if (byName == 0)
                {
                    byName = StringComparer.Ordinal.Compare(left[index], right[index]);
                }

                if (byName != 0)
                {
                    return byName;
                }
            }

            return left.Length.CompareTo(right.Length);
        }
    }
}
