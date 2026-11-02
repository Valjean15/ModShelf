using Microsoft.Extensions.Logging;

namespace ModShelf.Infrastructure.Pictures;

/// <remarks>Only the site's host is logged, never a picture's address.</remarks>
internal sealed partial class DiskPictureCache
{
    [LoggerMessage(EventId = 1500, Level = LogLevel.Information, Message = "A picture from {Host} couldn't be downloaded: {Reason}")]
    private static partial void LogCantDownload(ILogger logger, string host, string reason);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Information, Message = "{Host} refused a picture with status {Status}")]
    private static partial void LogRefused(ILogger logger, string host, int status);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Warning, Message = "{Host} sent {Type} instead of a picture, so it isn't kept")]
    private static partial void LogNotAPicture(ILogger logger, string host, string type);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Warning, Message = "A picture from {Host} is larger than {Limit} bytes, so it isn't kept")]
    private static partial void LogTooLarge(ILogger logger, string host, long limit);

    [LoggerMessage(EventId = 1504, Level = LogLevel.Information, Message = "Dropped {Count} saved pictures shown least recently, to keep them within {Megabytes} MB")]
    private static partial void LogTrimmed(ILogger logger, int count, long megabytes);

    [LoggerMessage(EventId = 1505, Level = LogLevel.Information, Message = "A saved picture couldn't be removed: {Reason}")]
    private static partial void LogCantDelete(ILogger logger, string reason);
}
