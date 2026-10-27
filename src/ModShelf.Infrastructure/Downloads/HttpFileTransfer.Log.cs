using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;

namespace ModShelf.Infrastructure.Downloads;

/// <summary>
/// The file transfer's log messages. They name the host and the file name only: never the URL, its query
/// string or the request headers, which can contain keys and signatures.
/// </summary>
internal sealed partial class HttpFileTransfer
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Download from {Host} was refused with HTTP {StatusCode}; the link needs refreshing")]
    private static partial void LogLinkRejected(ILogger logger, string host, HttpStatusCode statusCode);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Download from {Host} failed with HTTP {StatusCode}")]
    private static partial void LogServerError(ILogger logger, string host, HttpStatusCode statusCode);

    [LoggerMessage(EventId = 102, Level = LogLevel.Warning, Message = "Couldn't reach {Host}")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception, string host);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Connection to {Host} dropped at byte {Offset}; resuming (attempt {Attempt})")]
    private static partial void LogResuming(ILogger logger, string host, long offset, int attempt);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "Gave up on {Host} after {Attempts} dropped connections")]
    private static partial void LogTooManyInterruptions(ILogger logger, string host, int attempts);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information, Message = "Discarded the partial download of {FileName}: {Reason}")]
    private static partial void LogPartialFileDiscarded(ILogger logger, string fileName, string reason);

    [LoggerMessage(EventId = 106, Level = LogLevel.Warning, Message = "{FileName} already exists, so it was not overwritten")]
    private static partial void LogDestinationExists(ILogger logger, string fileName);

    [LoggerMessage(EventId = 107, Level = LogLevel.Error, Message = "Couldn't write {FileName}")]
    private static partial void LogStorageFailed(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 108, Level = LogLevel.Warning, Message = "{FileName} doesn't match its expected {Algorithm} hash; the download was discarded")]
    private static partial void LogHashMismatch(ILogger logger, string fileName, FileHashAlgorithm algorithm);

    [LoggerMessage(EventId = 109, Level = LogLevel.Warning, Message = "There isn't room on the disk for {FileName}: it needs {NeededBytes} more bytes, and {FreeBytes} are free")]
    private static partial void LogNotEnoughSpace(ILogger logger, string fileName, long neededBytes, long freeBytes);
}
