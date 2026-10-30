using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;

namespace ModShelf.Application.Releases;

internal sealed partial class AppUninstall
{
    [LoggerMessage(EventId = 1304, Level = LogLevel.Warning, Message = "Uninstalling, ModShelf couldn't tell which app opens the sites' links")]
    private static partial void LogCantList(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Warning, Message = "Uninstalling, ModShelf couldn't give {Source}'s {Scheme}:// links back")]
    private static partial void LogCantGiveBack(ILogger logger, Exception exception, SourceId source, string scheme);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Warning, Message = "Uninstalling, ModShelf couldn't remove what it registered for itself")]
    private static partial void LogCantRemove(ILogger logger, Exception exception);
}
