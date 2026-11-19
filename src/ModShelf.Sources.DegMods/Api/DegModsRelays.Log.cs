using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// The relays' log messages.
/// </summary>
internal sealed partial class DegModsRelays
{
    [LoggerMessage(EventId = 2020, Level = LogLevel.Warning, Message = "DEG Mods' relay {Relay} couldn't be read; what the others hold is used")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception, string relay);

    [LoggerMessage(
        EventId = 2021,
        Level = LogLevel.Warning,
        Message = "DEG Mods' relay {Relay} didn't answer in time, so it isn't asked for {Minutes} minutes; what the others hold is used")]
    private static partial void LogRelayRests(ILogger logger, Exception exception, string relay, double minutes);

    [LoggerMessage(EventId = 2022, Level = LogLevel.Information, Message = "DEG Mods' relay {Relay} answers again")]
    private static partial void LogRelayAnswers(ILogger logger, string relay);
}
