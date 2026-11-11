using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.ModIo.Api;

/// <summary>
/// The handler's log messages.
/// </summary>
internal sealed partial class ModIoSlowDownHandler
{
    [LoggerMessage(EventId = 1901, Level = LogLevel.Warning, Message = "mod.io asked ModShelf to slow down: it sends mod.io nothing until {Until:O}")]
    private static partial void LogSlowingDown(ILogger logger, DateTimeOffset until);
}
