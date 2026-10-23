using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// The rate limit's log messages.
/// </summary>
internal sealed partial class NexusQuotaHandler
{
    [LoggerMessage(
        EventId = 500,
        Level = LogLevel.Warning,
        Message = "Held back a Nexus Mods request: the API's rate limit for this account is used up until {Until:u}")]
    private static partial void LogHeldBack(ILogger logger, DateTimeOffset until);

    [LoggerMessage(
        EventId = 501,
        Level = LogLevel.Warning,
        Message = "Nexus Mods refused a request for its rate limit; requests are held back until {Until:u}")]
    private static partial void LogRefusedForRateLimit(ILogger logger, DateTimeOffset? until);
}
