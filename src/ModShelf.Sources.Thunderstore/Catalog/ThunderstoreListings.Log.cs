using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// The listings' log messages.
/// </summary>
internal sealed partial class ThunderstoreListings
{
    [LoggerMessage(EventId = 2600, Level = LogLevel.Information, Message = "Read Thunderstore's listing of {Community}: {Count} packages in {Chunks} chunks")]
    private static partial void LogRead(ILogger logger, string community, int count, int chunks);

    [LoggerMessage(EventId = 2601, Level = LogLevel.Debug, Message = "Thunderstore's listing of {Community} is unchanged since {LastModified}")]
    private static partial void LogUnchanged(ILogger logger, string community, DateTimeOffset? lastModified);

    [LoggerMessage(EventId = 2602, Level = LogLevel.Warning, Message = "Thunderstore's listing of {Community} couldn't be read again; the one read at {ReadAt} stays in use")]
    private static partial void LogKeptOld(ILogger logger, Exception exception, string community, DateTimeOffset readAt);

    [LoggerMessage(EventId = 2603, Level = LogLevel.Warning, Message = "Left out {Count} packages of Thunderstore's listing of {Community} that have no version or a name it doesn't give")]
    private static partial void LogLeftOut(ILogger logger, int count, string community);
}
