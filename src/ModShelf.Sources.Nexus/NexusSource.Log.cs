using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The source's log messages.
/// </summary>
public sealed partial class NexusSource
{
    [LoggerMessage(EventId = 502, Level = LogLevel.Warning, Message = "Nexus Mods didn't say where games' pictures are, so games show without them until ModShelf starts again: {Reason}")]
    private static partial void LogArtworkUnread(ILogger logger, string reason);
}
