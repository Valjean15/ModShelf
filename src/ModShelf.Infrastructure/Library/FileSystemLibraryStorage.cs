using System.Security.Cryptography;

using ModShelf.Application.Library.Ports;
using ModShelf.Domain.Common;
using ModShelf.Infrastructure.Downloads;

namespace ModShelf.Infrastructure.Library;

/// <summary>
/// The library's files, on this system's file system.
/// </summary>
internal sealed class FileSystemLibraryStorage : ILibraryStorage
{
    /// <summary>
    /// The longest path Windows takes in most programs: 260 characters, less the one that ends it.
    /// </summary>
    /// <remarks>
    /// .NET can go past it, but Explorer, archivers and many mod managers can't, so a file beyond it couldn't be used.
    /// </remarks>
    internal const int MaxWindowsPath = 259;

    // The start of the name of the file that marks a folder while it's looked for under another name, and of a copy
    // under way.
    private const string MarkPrefix = ".modshelf-";

    // Read and written a megabyte at a time, as a download is.
    private const int CopyBufferSize = 1024 * 1024;

    private readonly bool _isWindows;
    private readonly Func<string> _markName;

    public FileSystemLibraryStorage()
        : this(OperatingSystem.IsWindows())
    {
    }

    /// <param name="isWindows">Whether Windows' limits apply. Tests check them on any system.</param>
    /// <param name="markName">The name of the file that marks a folder while it's looked for under another name.</param>
    internal FileSystemLibraryStorage(bool isWindows, Func<string>? markName = null)
    {
        _isWindows = isWindows;
        _markName = markName ?? (() => $"{MarkPrefix}{Guid.NewGuid():N}.tmp");
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool IsSameFolder(string first, string second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        if (!Directory.Exists(first) || !Directory.Exists(second))
        {
            return false;
        }

        // A file put in the first is in the second only when they're one folder, whatever names, links, drives or
        // file system rules lead there. It's deleted as it's closed.
        string name = _markName();
        try
        {
            using FileStream mark = new(
                Path.Combine(first, name), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.DeleteOnClose);
            return File.Exists(Path.Combine(second, name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder this user can't write in: compared by name, as this system does, with its links followed.
            return FilePaths.Same(Downloads.FreeSpace.RealPath(first), Downloads.FreeSpace.RealPath(second));
        }
    }

    public long? FileSize(string path)
    {
        FileInfo file = new(path);
        return file.Exists ? file.Length : null;
    }

    public IReadOnlyList<string> FilesIn(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // The files a download under way writes, a copy under way, and the mark that compares folders, aren't the library's.
        return Directory.Exists(path)
            ? [.. Directory.EnumerateFiles(path).Where(file => !PartialFiles.IsPartial(file) && !IsMark(file)).Order(StringComparer.Ordinal)]
            : [];
    }

    public async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileStream file = OpenToRead(path);
        await using (file.ConfigureAwait(false))
        {
            byte[] digest = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(digest);
        }
    }

    public async Task<FileHashes> HashesAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileStream file = OpenToRead(path);
        await using (file.ConfigureAwait(false))
        {
            using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // MD5 isn't for security here: it's how a site such as Nexus Mods names a file's content.
            using IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            byte[] buffer = new byte[CopyBufferSize];
            int read;
            while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                sha256.AppendData(buffer, 0, read);
                md5.AppendData(buffer, 0, read);
            }

            return new FileHashes(Convert.ToHexStringLower(sha256.GetHashAndReset()), Convert.ToHexStringLower(md5.GetHashAndReset()));
        }
    }

    public async Task<byte[]> ReadSmallFileAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        FileStream file = OpenToRead(path);
        await using (file.ConfigureAwait(false))
        {
            if (file.Length > maxBytes)
            {
                throw new IOException($"{Path.GetFileName(path)} has {file.Length} bytes, more than the {maxBytes} read.");
            }

            byte[] bytes = new byte[file.Length];
            await file.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }
    }

    public async Task CopyFileAsync(string source, string destination, string sha256, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        if (File.Exists(destination))
        {
            throw new IOException($"There's a file at {destination} already, which a copy never replaces.");
        }

        // A partial file a rescan doesn't list, in the same folder, so it becomes the file by a rename.
        string folder = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(folder);
        string partial = Path.Combine(folder, $"{MarkPrefix}copy-{Guid.NewGuid():N}.tmp");
        try
        {
            string copied = await CopyToAsync(source, partial, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(copied, sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{Path.GetFileName(source)} changed since it was read, so it wasn't copied.");
            }

            File.Move(partial, destination, overwrite: false);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public long? FreeSpace(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        return Downloads.FreeSpace.Available(folder);
    }

    public bool SameDrive(string first, string second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        return Downloads.FreeSpace.SameVolume(first, second);
    }

    public void EnsurePathFits(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // While it downloads, the file has a longer name.
        if (_isWindows && PartialFiles.PathFor(path).Length > MaxWindowsPath)
        {
            throw new PathTooLongException(
                $"{path} is too long for Windows, where a file in the library can have {MaxWindowsPath - PartialFiles.Extension.Length} "
                + "characters at most. Choose a shorter library root, or a folder pattern with fewer or shorter parts.");
        }
    }

    public void MoveFile(string source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.Move(source, destination, overwrite: false);
    }

    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // File.Delete does nothing for a file that isn't there, but throws for a folder that isn't.
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool RemoveFolderIfEmpty(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (!Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
            {
                return false;
            }

            Directory.Delete(path, recursive: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Something came into it, or it's in use: it stays.
            return false;
        }
    }

    private static bool IsMark(string path) => Path.GetFileName(path).StartsWith(MarkPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Opens a file to read once, front to back, sharing it with programs that read it too.
    /// </summary>
    private static FileStream OpenToRead(string path)
        => new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

    /// <summary>
    /// Copies the file at <paramref name="source"/> to a new one at <paramref name="partial"/>, reading it once.
    /// </summary>
    /// <returns>The SHA-256 of what was copied.</returns>
    private static async Task<string> CopyToAsync(string source, string partial, CancellationToken cancellationToken)
    {
        FileStream from = OpenToRead(source);
        await using (from.ConfigureAwait(false))
        {
            FileStream to = new(partial, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
            await using (to.ConfigureAwait(false))
            {
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[CopyBufferSize];
                int read;
                while ((read = await from.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await to.FlushAsync(cancellationToken).ConfigureAwait(false);
                return Convert.ToHexStringLower(hash.GetHashAndReset());
            }
        }
    }

    /// <summary>
    /// Deletes a partial copy, which isn't the user's: when it can't be, it stays, hidden from rescans.
    /// </summary>
    private static void TryDelete(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind, under a name no rescan lists.
        }
    }
}
