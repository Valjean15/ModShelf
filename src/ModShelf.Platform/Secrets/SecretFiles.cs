namespace ModShelf.Platform.Secrets;

/// <summary>
/// File handling for the stores that keep one file per secret: owner-only permissions on Unix, and writes that
/// never leave a half-written file behind.
/// </summary>
internal static class SecretFiles
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Creates <paramref name="folder"/> if it's missing; on Unix, readable only by the current user.
    /// </summary>
    public static void EnsureFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, OwnerOnlyFolder);
        }
    }

    /// <summary>
    /// Writes the file under a temporary name, then renames it into place in one step, so a crash leaves either
    /// the old secret or the new one. On Unix the file is readable only by the current user.
    /// </summary>
    public static async Task WriteAsync(string path, byte[] contents, CancellationToken cancellationToken)
    {
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFile;
        }

        try
        {
            FileStream file = new(temporary, options);
            await using (file.ConfigureAwait(false))
            {
                await file.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Deletes the file. Does nothing when the file or its folder doesn't exist.
    /// </summary>
    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was ever stored, so there's nothing to delete.
        }
    }

    /// <returns>The file's contents, or null when there's no such file.</returns>
    public static async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
