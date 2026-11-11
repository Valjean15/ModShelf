using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// The relays' log messages.
/// </summary>
internal sealed partial class DegModsRelays
{
    [LoggerMessage(EventId = 2020, Level = LogLevel.Warning, Message = "DEG Mods' relay {Relay} couldn't be read; what the others hold is used")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception, string relay);
}
