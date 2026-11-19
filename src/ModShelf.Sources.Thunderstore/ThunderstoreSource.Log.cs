using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// The source's log messages.
/// </summary>
public sealed partial class ThunderstoreSource
{
    [LoggerMessage(EventId = 2610, Level = LogLevel.Information, Message = "Read Thunderstore's communities: {Count}")]
    private static partial void LogCommunitiesRead(ILogger logger, int count);

    [LoggerMessage(EventId = 2611, Level = LogLevel.Warning, Message = "Thunderstore's package {Package} in {Community} names a dependency that isn't Team-Name-1.2.3; it's left out")]
    private static partial void LogOddDependency(ILogger logger, string package, string community);
}
