using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads.Ports;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// Downloads files over HTTP. Bytes go to a <c>.part</c> file next to the destination, which a later attempt
/// continues with a Range request. Once complete and verified, the file is renamed into place in one step.
/// </summary>
/// <remarks>
/// Beside the partial file, a <c>.part.info</c> file keeps what the server said of the file: its length, and its entity
/// tag or the date it last changed. A later attempt asks for the rest only if the file is still that one
/// (<c>If-Range</c>), and starts over when the server's answer is for a file of another length. An answer that's a web
/// page, such as a sign-in page or a captive portal, is never saved as the file.
/// </remarks>
/// <remarks>
/// Transient request failures are retried by the HTTP client's resilience pipeline (see
/// <see cref="InfrastructureServiceCollectionExtensions.AddHttpFileTransfer"/>). A connection that drops or
/// stalls mid-file is resumed here, from the bytes already on disk.
/// </remarks>
internal sealed partial class HttpFileTransfer : IFileTransfer
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> this transfer asks <see cref="IHttpClientFactory"/> for.
    /// </summary>
    public const string HttpClientName = "ModShelf.FileTransfer";

    private const int BufferSize = 81920;

    // How many times the finished file is moved into place while another program holds it for a moment.
    private const int MoveAttempts = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly FileTransferOptions _options;
    private readonly ILogger<HttpFileTransfer> _logger;
    private readonly Func<string, long?> _freeSpace;
    private readonly Action<string, string> _move;
    private readonly Action<string> _delete;

    public HttpFileTransfer(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        FileTransferOptions options,
        ILogger<HttpFileTransfer> logger)
        : this(httpClientFactory, timeProvider, options, logger, FreeSpace.Available)
    {
    }

    /// <param name="freeSpace">
    /// The bytes free on the disk that holds a folder, or null when the system can't tell. Tests stand in for the disk.
    /// </param>
    /// <param name="move">Moves the finished file into place without overwriting; null for the system's own move.</param>
    /// <param name="delete">Deletes a partial file; null for the system's own delete.</param>
    internal HttpFileTransfer(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        FileTransferOptions options,
        ILogger<HttpFileTransfer> logger,
        Func<string, long?> freeSpace,
        Action<string, string>? move = null,
        Action<string>? delete = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ReadTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxResumeAttempts);
        ArgumentNullException.ThrowIfNull(freeSpace);

        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
        _freeSpace = freeSpace;
        _move = move ?? ((from, to) => File.Move(from, to, overwrite: false));
        _delete = delete ?? File.Delete;
    }

    private enum ResponseKind
    {
        /// <summary>The body continues the partial file, or replaces it from byte 0.</summary>
        Content,

        /// <summary>The partial file already holds the whole file.</summary>
        AlreadyComplete,

        /// <summary>The server can't continue the partial file, so it has to start over.</summary>
        CannotResume,

        /// <summary>The ticket is no longer accepted.</summary>
        LinkRejected,

        /// <summary>Any other failure, after the pipeline's retries.</summary>
        ServerError,
    }

    private enum CopyResult
    {
        Completed,
        Interrupted,
        StorageFailed,
    }

    public async Task<TransferOutcome> DownloadAsync(
        DownloadTicket ticket,
        string destinationPath,
        ITransferProgress progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(progress);

        string fileName = Path.GetFileName(destinationPath);
        if (File.Exists(destinationPath))
        {
            LogDestinationExists(_logger, fileName);
            return TransferOutcome.StorageError;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            LogStorageFailed(_logger, exception, fileName);
            return TransferOutcome.StorageError;
        }

        string partialPath = PartialFiles.PathFor(destinationPath);
        TransferOutcome? failure = await FetchAsync(ticket, partialPath, fileName, progress, cancellationToken).ConfigureAwait(false);
        if (failure is { } outcome)
        {
            return outcome;
        }

        progress.Verifying();
        return await VerifyAndMoveAsync(ticket.ExpectedHash, partialPath, destinationPath, fileName, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task DeletePartialFileAsync(string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string partialPath = PartialFiles.PathFor(destinationPath);
        try
        {
            _delete(partialPath);
        }
        catch (DirectoryNotFoundException)
        {
            // The download never got as far as creating its folder, so there's no partial file.
        }

        PartialFiles.DeleteRecord(partialPath);
        return Task.CompletedTask;
    }

    /// <param name="record">What the server said of the file the partial file holds the start of, when that was kept.</param>
    private static HttpRequestMessage CreateRequest(DownloadTicket ticket, long offset, PartialFileRecord? record)
    {
        HttpRequestMessage request = new(HttpMethod.Get, ticket.Url);
        foreach (KeyValuePair<string, string> header in ticket.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);

            // The rest only if the file is still the one started; otherwise the server sends the whole of the new one.
            if (record?.ETag is { } tag)
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(tag));
            }
            else if (record?.LastModified is { } changed)
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(changed);
            }
        }

        return request;
    }

    private static ResponseKind Classify(HttpResponseMessage response, long offset, PartialFileRecord? record)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone:
                return ResponseKind.LinkRejected;

            case HttpStatusCode.RequestedRangeNotSatisfiable:
                // "bytes */1000": a partial file of exactly that length is complete, if the file started was that long
                // too; any other is unusable.
                return offset > 0
                    && response.Content.Headers.ContentRange?.Length == offset
                    && (record?.Length is null || record.Length == offset)
                    ? ResponseKind.AlreadyComplete
                    : ResponseKind.CannotResume;

            case HttpStatusCode.PartialContent:
                // The rest of a file of another length is the rest of another file.
                return response.Content.Headers.ContentRange is { HasRange: true } range
                    && range.From == offset
                    && (record?.Length is null || range.Length is null || range.Length == record.Length)
                    ? ResponseKind.Content
                    : ResponseKind.CannotResume;

            case HttpStatusCode.OK:
                return ResponseKind.Content;

            default:
                return ResponseKind.ServerError;
        }
    }

    private static bool IsNetworkFailure(Exception exception, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or IOException or OperationCanceledException;

    private static bool IsStorageFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException;

    /// <summary>
    /// Whether the answer is a web page, such as a sign-in page, a captive portal, or a file host's page about the file,
    /// when the file isn't one.
    /// </summary>
    private static bool IsWebPage(HttpResponseMessage response, string fileName)
        => string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The answer's entity tag, when it's a strong one: a weak one doesn't stand for the bytes.
    /// </summary>
    private static string? StrongTag(HttpResponseMessage response)
        => response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null;

    private static async Task<FileHash> ComputeHashAsync(string path, FileHashAlgorithm algorithm, CancellationToken cancellationToken)
    {
        FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (file.ConfigureAwait(false))
        {
            byte[] digest = await CryptographicOperations
                .HashDataAsync(HashAlgorithmFor(algorithm), file, cancellationToken)
                .ConfigureAwait(false);
            return FileHash.FromDigest(algorithm, digest);
        }
    }

    private static HashAlgorithmName HashAlgorithmFor(FileHashAlgorithm algorithm) => algorithm switch
    {
        FileHashAlgorithm.Md5 => HashAlgorithmName.MD5,
        FileHashAlgorithm.Sha1 => HashAlgorithmName.SHA1,
        FileHashAlgorithm.Sha256 => HashAlgorithmName.SHA256,
        FileHashAlgorithm.Sha512 => HashAlgorithmName.SHA512,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown hash algorithm."),
    };

    /// <summary>
    /// Gets every byte of the file into the partial file.
    /// </summary>
    /// <returns>Null once all bytes are on disk; otherwise the outcome to report.</returns>
    private async Task<TransferOutcome?> FetchAsync(
        DownloadTicket ticket,
        string partialPath,
        string fileName,
        ITransferProgress progress,
        CancellationToken cancellationToken)
    {
        string host = ticket.Url.Host;
        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        ProgressReporter reporter = new(progress, _timeProvider, _options.ProgressInterval);
        bool started = false;
        int interruptions = 0;

        while (true)
        {
            long offset = PartialFiles.LengthOf(partialPath);
            PartialFileRecord? record = offset > 0 ? PartialFiles.ReadRecord(partialPath) : null;
            using HttpRequestMessage request = CreateRequest(ticket, offset, record);

            HttpResponseMessage response;
            try
            {
                response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
            {
                // The resilience pipeline has already retried, so the connection keeps failing.
                LogConnectionFailed(_logger, exception, host);
                return TransferOutcome.NetworkError;
            }

            using (response)
            {
                switch (Classify(response, offset, record))
                {
                    case ResponseKind.LinkRejected:
                        LogLinkRejected(_logger, host, response.StatusCode);
                        return TransferOutcome.LinkExpired;

                    case ResponseKind.ServerError:
                        LogServerError(_logger, host, response.StatusCode);
                        return TransferOutcome.NetworkError;

                    case ResponseKind.AlreadyComplete:
                        if (!started)
                        {
                            progress.Started(offset, offset);
                        }

                        reporter.Report(offset);
                        reporter.Flush();
                        return null;

                    case ResponseKind.CannotResume:
                        LogPartialFileDiscarded(_logger, fileName, "the server can't continue it");
                        try
                        {
                            DiscardPartialFile(partialPath);
                        }
                        catch (Exception exception) when (IsStorageFailure(exception))
                        {
                            // Such as a partial file another program holds.
                            LogStorageFailed(_logger, exception, fileName);
                            return TransferOutcome.StorageError;
                        }

                        if (++interruptions > _options.MaxResumeAttempts)
                        {
                            LogTooManyInterruptions(_logger, host, interruptions);
                            return TransferOutcome.NetworkError;
                        }

                        continue;
                }

                // Checked before the partial file is touched, so a captive portal's page doesn't replace it.
                if (IsWebPage(response, fileName))
                {
                    LogWebPage(_logger, host, fileName);
                    return TransferOutcome.LinkExpired;
                }

                bool append = response.StatusCode == HttpStatusCode.PartialContent;
                long position = append ? offset : 0;
                long? totalBytes = append ? response.Content.Headers.ContentRange!.Length : response.Content.Headers.ContentLength;
                if (!append && offset > 0)
                {
                    LogPartialFileDiscarded(_logger, fileName, "the server sent the whole file again");
                }

                // Checked before any byte is written, so a file that can't fit leaves the disk as it was. A partial file
                // the body replaces frees its bytes first.
                long? missing = totalBytes - position;
                long? room = _freeSpace(Path.GetDirectoryName(partialPath)!) + (append ? 0 : offset);
                if (missing > room)
                {
                    LogNotEnoughSpace(_logger, fileName, missing.Value, room.Value);
                    return TransferOutcome.StorageError;
                }

                // Before the first byte, so that a later attempt can tell whether the server's file is still this one.
                try
                {
                    PartialFiles.WriteRecord(
                        partialPath,
                        new PartialFileRecord(totalBytes, StrongTag(response), response.Content.Headers.LastModified));
                }
                catch (Exception exception) when (IsStorageFailure(exception))
                {
                    LogStorageFailed(_logger, exception, fileName);
                    return TransferOutcome.StorageError;
                }

                if (!started)
                {
                    progress.Started(position, totalBytes);
                    started = true;
                }
                else
                {
                    reporter.Report(position);
                }

                CopyResult copy = await CopyAsync(response, partialPath, fileName, append, position, reporter, cancellationToken)
                    .ConfigureAwait(false);
                if (copy == CopyResult.StorageFailed)
                {
                    return TransferOutcome.StorageError;
                }

                if (copy == CopyResult.Completed && (totalBytes is null || PartialFiles.LengthOf(partialPath) == totalBytes))
                {
                    reporter.Flush();
                    return null;
                }

                // The connection dropped, stalled, or ended before the whole file arrived.
                reporter.Flush();
                if (++interruptions > _options.MaxResumeAttempts)
                {
                    LogTooManyInterruptions(_logger, host, interruptions);
                    return TransferOutcome.NetworkError;
                }

                long resumeAt = PartialFiles.LengthOf(partialPath);
                LogResuming(_logger, host, resumeAt, interruptions);
            }
        }
    }

    private async Task<CopyResult> CopyAsync(
        HttpResponseMessage response,
        string partialPath,
        string fileName,
        bool append,
        long position,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        FileStream file;
        try
        {
            // Unbuffered, so the partial file's length always matches the bytes received.
            file = new FileStream(
                partialPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 0,
                FileOptions.Asynchronous);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            LogStorageFailed(_logger, exception, fileName);
            return CopyResult.StorageFailed;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using (file.ConfigureAwait(false))
            {
                Stream body;
                try
                {
                    body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
                {
                    return CopyResult.Interrupted;
                }

                await using (body.ConfigureAwait(false))
                {
                    using CancellationTokenSource readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    while (true)
                    {
                        int read;
                        readTimeout.CancelAfter(_options.ReadTimeout);
                        try
                        {
                            read = await body.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
                        {
                            return CopyResult.Interrupted;
                        }

                        if (read == 0)
                        {
                            return CopyResult.Completed;
                        }

                        try
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (IsStorageFailure(exception))
                        {
                            LogStorageFailed(_logger, exception, fileName);
                            return CopyResult.StorageFailed;
                        }

                        position += read;
                        reporter.Report(position);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<TransferOutcome> VerifyAndMoveAsync(
        FileHash? expectedHash,
        string partialPath,
        string destinationPath,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            if (expectedHash is not null)
            {
                FileHash actual = await ComputeHashAsync(partialPath, expectedHash.Algorithm, cancellationToken).ConfigureAwait(false);
                if (actual != expectedHash)
                {
                    LogHashMismatch(_logger, fileName, expectedHash.Algorithm);
                    DiscardPartialFile(partialPath);
                    return TransferOutcome.HashMismatch;
                }
            }

            // Never overwrite: if a file appeared at the destination meanwhile, this throws and nothing is lost.
            await MoveIntoPlaceAsync(partialPath, destinationPath, cancellationToken).ConfigureAwait(false);
            PartialFiles.DeleteRecord(partialPath);
            return TransferOutcome.Completed;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            LogStorageFailed(_logger, exception, fileName);
            return TransferOutcome.StorageError;
        }
    }

    /// <summary>
    /// Moves the finished file into place, trying again a few times, a moment apart, while another program holds it,
    /// such as a virus scanner looking at it.
    /// </summary>
    private async Task MoveIntoPlaceAsync(string partialPath, string destinationPath, CancellationToken cancellationToken)
    {
        TimeSpan wait = TimeSpan.FromMilliseconds(100);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _move(partialPath, destinationPath);
                return;
            }
            catch (IOException) when (attempt < MoveAttempts && File.Exists(partialPath) && !File.Exists(destinationPath))
            {
                await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
                wait *= 2;
            }
        }
    }

    private void DiscardPartialFile(string partialPath)
    {
        _delete(partialPath);
        PartialFiles.DeleteRecord(partialPath);
    }
}
