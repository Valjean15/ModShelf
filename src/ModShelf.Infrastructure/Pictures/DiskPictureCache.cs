using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Diagnostics.Ports;
using ModShelf.Application.Pictures;
using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;

namespace ModShelf.Infrastructure.Pictures;

/// <summary>
/// Keeps pictures in a <c>pictures</c> folder beside the library database, each under the SHA-256 of its address. A
/// picture shown is touched, so the ones shown least recently go first when the folder outgrows the room the settings
/// give it; trimming leaves it at nine tenths of that, so it isn't trimmed again for every picture.
/// </summary>
internal sealed partial class DiskPictureCache : IPictureCache, IDisposable
{
    internal const string HttpClientName = "ModShelf.Pictures";

    /// <summary>
    /// The largest picture kept. Sites' thumbnails are tens of kilobytes, and full pictures a few megabytes.
    /// </summary>
    internal const long MaxPictureBytes = 10 * 1024 * 1024;

    /// <summary>
    /// How many pictures download at once: a screen showing many mods doesn't ask a site for all their pictures together.
    /// </summary>
    internal const int DownloadsAtOnce = 6;

    private const string PartialExtension = ".part";

    /// <summary>
    /// How long one picture's download may take, from asking to the last byte.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _http;
    private readonly ISettingsStore _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<DiskPictureCache> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _downloads = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _downloading = new(DownloadsAtOnce, DownloadsAtOnce);
    private readonly SemaphoreSlim _trimming = new(1, 1);

    public DiskPictureCache(IHttpClientFactory http, ISettingsStore settings, IAppFolders folders, TimeProvider time, ILogger<DiskPictureCache> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _settings = settings;
        _time = time;
        _logger = logger;
        Folder = Path.Combine(folders.DataFolder, "pictures");
    }

    /// <summary>
    /// The folder the pictures are saved in.
    /// </summary>
    internal string Folder { get; }

    public async Task<string?> GetAsync(Uri picture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(picture);
        if (!picture.IsAbsoluteUri || (picture.Scheme != Uri.UriSchemeHttps && picture.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("A picture's address must be an absolute HTTP or HTTPS one.", nameof(picture));
        }

        string name = NameOf(picture);
        string path = Path.Combine(Folder, name);

        // On the thread pool: a screen asks for a picture of each mod it shows, from its UI thread (ADR 0084).
        if (await Task.Run(() => IsSaved(path), cancellationToken).ConfigureAwait(false))
        {
            return path;
        }

        // One download for every caller: a caller that stops waiting doesn't stop it for the others.
        Lazy<Task<string?>> download = _downloads.GetOrAdd(name, _ => new Lazy<Task<string?>>(() => DownloadAsync(picture, name, path)));
        return await download.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<long> GetSizeAsync(CancellationToken cancellationToken)
        => Task.Run(() => Saved().Sum(file => file.Length), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                foreach (FileInfo file in Saved())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Delete(file);
                }
            },
            cancellationToken);

    public void Dispose()
    {
        _downloading.Dispose();
        _trimming.Dispose();
    }

    /// <summary>
    /// A picture's file name: the SHA-256 of its address, which says nothing about it and can't clash.
    /// </summary>
    internal static string NameOf(Uri picture)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(picture.AbsoluteUri)));

    private async Task<string?> DownloadAsync(Uri picture, string name, string path)
    {
        try
        {
            await _downloading.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!await FetchAsync(picture, path).ConfigureAwait(false))
                {
                    return null;
                }
            }
            finally
            {
                _downloading.Release();
            }

            // Shown now, on the same clock as the pictures shown before it.
            Touch(path);
            await TrimAsync(name).ConfigureAwait(false);
            return path;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Offline, a site that's slow or gone, or a full disk: the picture is asked for again next time.
            LogCantDownload(_logger, picture.Host, exception.Message);
            return null;
        }
        finally
        {
            _downloads.TryRemove(name, out _);
        }
    }

    /// <returns>Whether the picture was saved; false, logged, when the site sent no picture or one too large.</returns>
    private async Task<bool> FetchAsync(Uri picture, string path)
    {
        using CancellationTokenSource timeout = new(Patience, _time);
        using HttpClient client = _http.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client
            .GetAsync(picture, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            LogRefused(_logger, picture.Host, (int)response.StatusCode);
            return false;
        }

        if (response.Content.Headers.ContentType?.MediaType is not { } type
            || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            LogNotAPicture(_logger, picture.Host, response.Content.Headers.ContentType?.MediaType ?? "nothing");
            return false;
        }

        if (response.Content.Headers.ContentLength > MaxPictureBytes)
        {
            LogTooLarge(_logger, picture.Host, MaxPictureBytes);
            return false;
        }

        Directory.CreateDirectory(Folder);
        Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            if (!await SaveAsync(body, path, timeout.Token).ConfigureAwait(false))
            {
                LogTooLarge(_logger, picture.Host, MaxPictureBytes);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes the picture beside its place, then puts it there in one step, so a picture half-written is never shown.
    /// </summary>
    /// <returns>False when it's larger than <see cref="MaxPictureBytes"/>, which leaves nothing behind.</returns>
    private static async Task<bool> SaveAsync(Stream body, string path, CancellationToken cancellationToken)
    {
        string partial = $"{path}.{Guid.NewGuid():N}{PartialExtension}";
        try
        {
            FileStream file = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using (file.ConfigureAwait(false))
            {
                byte[] buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > MaxPictureBytes)
                    {
                        return false;
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(partial, path, overwrite: true);
            return true;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>
    /// Drops the pictures shown least recently while the saved ones take more room than the settings give them, down to
    /// nine tenths of it; the picture just saved stays.
    /// </summary>
    private async Task TrimAsync(string justSaved)
    {
        await _trimming.WaitAsync().ConfigureAwait(false);
        try
        {
            long room = await RoomAsync().ConfigureAwait(false);
            List<FileInfo> saved = [.. Saved().OrderBy(file => file.LastWriteTimeUtc)];
            long size = saved.Sum(file => file.Length);
            if (size <= room)
            {
                return;
            }

            long target = room / 10 * 9;
            int dropped = 0;
            foreach (FileInfo file in saved.Where(file => !string.Equals(file.Name, justSaved, StringComparison.Ordinal)))
            {
                if (size <= target)
                {
                    break;
                }

                // Its size read before it goes: a file deleted has none to read.
                long length = file.Length;
                if (Delete(file))
                {
                    size -= length;
                    dropped++;
                }
            }

            LogTrimmed(_logger, dropped, room / (1024 * 1024));
        }
        finally
        {
            _trimming.Release();
        }
    }

    /// <summary>
    /// The room the settings give the pictures, or the default's when they can't be read.
    /// </summary>
    private async Task<long> RoomAsync()
    {
        int megabytes;
        try
        {
            megabytes = (await _settings.LoadAsync(CancellationToken.None).ConfigureAwait(false)).PicturesMegabytes ?? AppSettings.DefaultPicturesMegabytes;
        }
        catch (InvalidDataException)
        {
            megabytes = AppSettings.DefaultPicturesMegabytes;
        }

        return megabytes * 1024L * 1024L;
    }

    /// <summary>
    /// The pictures saved, without downloads under way.
    /// </summary>
    private IEnumerable<FileInfo> Saved()
    {
        DirectoryInfo folder = new(Folder);
        return folder.Exists
            ? folder.EnumerateFiles().Where(file => !file.Name.EndsWith(PartialExtension, StringComparison.Ordinal))
            : [];
    }

    /// <returns>Whether the picture is saved; marked as just shown when it is.</returns>
    private bool IsSaved(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        Touch(path);
        return true;
    }

    /// <summary>
    /// Marks a picture as just shown, so it's among the last to go.
    /// </summary>
    private void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Being shown now matters more than the order it goes in.
        }
    }

    /// <returns>Whether the picture is gone; one in use, which the system won't delete on Windows, stays.</returns>
    private bool Delete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCantDelete(_logger, exception.Message);
            return false;
        }
    }
}
