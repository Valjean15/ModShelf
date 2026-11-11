using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// The relay client's log messages.
/// </summary>
internal sealed partial class RelayClient
{
    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug, Message = "Read {Count} events from DEG Mods' relay {Relay} in {Requests} requests")]
    private static partial void LogRead(ILogger logger, int count, int requests, string relay);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug, Message = "DEG Mods' relay {Relay} refused a request: {Answer}")]
    private static partial void LogRefused(ILogger logger, Uri relay, string answer);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Debug, Message = "DEG Mods' relay {Relay} said: {Notice}")]
    private static partial void LogNotice(ILogger logger, Uri relay, string notice);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Warning, Message = "DEG Mods' relay {Relay} sent {Count} events that aren't what their authors signed; they're left out")]
    private static partial void LogNotGenuine(ILogger logger, int count, string relay);
}
