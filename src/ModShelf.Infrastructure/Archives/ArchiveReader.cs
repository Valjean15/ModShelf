using System.IO.Compression;

using ModShelf.Application.Contents;
using ModShelf.Application.Contents.Ports;

using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

using CryptographicException = SharpCompress.Common.CryptographicException;

namespace ModShelf.Infrastructure.Archives;

/// <summary>
/// Reads .zip archives with <see cref="System.IO.Compression"/>, and .7z and .rar archives with SharpCompress, where
/// they are: lists their entries and reads the start of one. Nothing is ever extracted or written, and an entry's path
/// is never made into a path on disk (ADR 0106). The work runs off the caller's thread, since neither reads
/// asynchronously throughout.
/// </summary>
internal sealed class ArchiveReader : IArchiveReader
{
    private const string Zip = "zip";
    private const string SevenZip = "7z";
    private const string Rar = "rar";

    public string? FormatOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return Path.GetExtension(fileName).ToUpperInvariant() switch
        {
            ".ZIP" => Zip,
            ".7Z" => SevenZip,
            ".RAR" => Rar,
            _ => null,
        };
    }

    public Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string format = FormatOf(path) ?? throw NotAnArchive(path);

        return Task.Run<IReadOnlyList<ArchiveEntry>>(
            () => Reading(path, format, () => format == Zip ? ListZip(path, cancellationToken) : ListOther(path, cancellationToken)),
            cancellationToken);
    }

    public Task<ArchiveBytes> ReadAsync(string path, string entry, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        string format = FormatOf(path) ?? throw NotAnArchive(path);

        return Task.Run(
            () => Reading(path, format, () => format == Zip ? ReadZip(path, entry, maxBytes, cancellationToken) : ReadOther(path, entry, maxBytes, cancellationToken)),
            cancellationToken);
    }

    private static List<ArchiveEntry> ListZip(string path, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        List<ArchiveEntry> entries = new(archive.Entries.Count);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool folder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            entries.Add(new ArchiveEntry(PathOf(entry.FullName), folder ? 0 : entry.Length, folder));
        }

        return entries;
    }

    private static List<ArchiveEntry> ListOther(string path, CancellationToken cancellationToken)
    {
        using IArchive archive = ArchiveFactory.OpenArchive(path, new ReaderOptions());
        List<ArchiveEntry> entries = [];
        foreach (IArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(new ArchiveEntry(PathOf(entry.Key ?? string.Empty), entry.IsDirectory ? 0 : entry.Size, entry.IsDirectory));
        }

        return entries;
    }

    private static ArchiveBytes ReadZip(string path, string wanted, int maxBytes, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = archive.Entries.FirstOrDefault(entry => !entry.FullName.EndsWith('/') && PathOf(entry.FullName) == wanted)
            ?? throw NoSuchEntry(path, wanted);
        if (entry.IsEncrypted)
        {
            throw Encrypted(path, wanted);
        }

        using Stream stream = entry.Open();
        return new ArchiveBytes(Start(stream, maxBytes, cancellationToken), entry.Length);
    }

    private static ArchiveBytes ReadOther(string path, string wanted, int maxBytes, CancellationToken cancellationToken)
    {
        using IArchive archive = ArchiveFactory.OpenArchive(path, new ReaderOptions());
        IArchiveEntry entry = archive.Entries.FirstOrDefault(entry => !entry.IsDirectory && PathOf(entry.Key ?? string.Empty) == wanted)
            ?? throw NoSuchEntry(path, wanted);
        if (entry.IsEncrypted)
        {
            throw Encrypted(path, wanted);
        }

        using Stream stream = entry.OpenEntryStream();
        return new ArchiveBytes(Start(stream, maxBytes, cancellationToken), entry.Size);
    }

    /// <summary>
    /// The first <paramref name="maxBytes"/> bytes of the stream, or all of them.
    /// </summary>
    private static byte[] Start(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[maxBytes];
        int total = 0;
        while (total < maxBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = stream.Read(buffer, total, maxBytes - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total == maxBytes ? buffer : buffer[..total];
    }

    /// <summary>
    /// An entry's path as listed: '/' between folders, without an empty or a <c>.</c> part. Shown, never used on disk.
    /// </summary>
    private static string PathOf(string key)
        => string.Join('/', key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(part => part != "."));

    /// <summary>
    /// Runs <paramref name="read"/>, telling an archive that needs a password, or that's damaged, from the file that
    /// can't be read, which stays as it is.
    /// </summary>
    private static T Reading<T>(string path, string format, Func<T> read)
    {
        string name = Path.GetFileName(path);
        try
        {
            return read();
        }
        catch (CryptographicException exception)
        {
            throw new ArchiveUnreadableException(ArchiveTrouble.Encrypted, $"{name} needs a password, which ModShelf doesn't ask for.", exception);
        }
        catch (Exception exception) when (exception is InvalidDataException or SharpCompressException or EndOfStreamException
            or InvalidOperationException or NotSupportedException or IndexOutOfRangeException or ArgumentException)
        {
            throw new ArchiveUnreadableException(ArchiveTrouble.Broken, $"{name} can't be read as a {format} archive: it's damaged, cut short, or isn't one.", exception);
        }
    }

    private static ArchiveUnreadableException NotAnArchive(string path)
        => new(ArchiveTrouble.NotAnArchive, $"{Path.GetFileName(path)} isn't an archive ModShelf can look into: it reads .zip, .7z and .rar files.");

    private static ArchiveUnreadableException Encrypted(string path, string entry)
        => new(ArchiveTrouble.Encrypted, $"{entry} in {Path.GetFileName(path)} needs a password, which ModShelf doesn't ask for.");

    private static KeyNotFoundException NoSuchEntry(string path, string entry)
        => new($"{Path.GetFileName(path)} has no file {entry}.");
}
