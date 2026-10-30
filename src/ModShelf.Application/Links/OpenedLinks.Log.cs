using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Links;

/// <summary>
/// The opened links' log messages. They never hold the link, which can carry a key.
/// </summary>
internal sealed partial class OpenedLinks
{
    [LoggerMessage(EventId = 1303, Level = LogLevel.Information, Message = "A {Source} link another app handed over went to {Where}")]
    private static partial void LogOpened(ILogger logger, string source, string where);
}
