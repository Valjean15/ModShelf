using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;

namespace ModShelf.Application.Links;

/// <summary>
/// The link handlers' log messages.
/// </summary>
internal sealed partial class LinkHandlers
{
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "ModShelf opens {Source}'s {Scheme}:// links now")]
    private static partial void LogTaken(ILogger logger, SourceId source, string scheme);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "ModShelf gave {Source}'s {Scheme}:// links back")]
    private static partial void LogGivenBack(ILogger logger, SourceId source, string scheme);
}
