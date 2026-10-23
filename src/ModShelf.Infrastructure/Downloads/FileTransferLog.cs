using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// The file transfer's log messages. They name the host and the file name only: never the URL, its query
/// string or the request headers, which can contain keys and signatures.
/// </summary>
internal static partial class FileTransferLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Download from {Host} was refused with HTTP {StatusCode}; the link needs refreshing")]
    public static partial void LinkRejected(this ILogger logger, string host, HttpStatusCode statusCode);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Download from {Host} failed with HTTP {StatusCode}")]
    public static partial void ServerError(this ILogger logger, string host, HttpStatusCode statusCode);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Couldn't reach {Host}")]
    public static partial void ConnectionFailed(this ILogger logger, Exception exception, string host);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Connection to {Host} dropped at byte {Offset}; resuming (attempt {Attempt})")]
    public static partial void Resuming(this ILogger logger, string host, long offset, int attempt);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "Gave up on {Host} after {Attempts} dropped connections")]
    public static partial void TooManyInterruptions(this ILogger logger, string host, int attempts);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information, Message = "Discarded the partial download of {FileName}: {Reason}")]
    public static partial void PartialFileDiscarded(this ILogger logger, string fileName, string reason);

    [LoggerMessage(EventId = 106, Level = LogLevel.Warning, Message = "{FileName} already exists, so it was not overwritten")]
    public static partial void DestinationExists(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 107, Level = LogLevel.Error, Message = "Couldn't write {FileName}")]
    public static partial void StorageFailed(this ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 108, Level = LogLevel.Warning, Message = "{FileName} doesn't match its expected {Algorithm} hash; the download was discarded")]
    public static partial void HashMismatch(this ILogger logger, string fileName, FileHashAlgorithm algorithm);
}
