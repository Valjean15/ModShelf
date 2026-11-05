using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Diagnostics;

internal sealed partial class DeprecatedCalls
{
    [LoggerMessage(EventId = 1700, Level = LogLevel.Warning, Message = "A call with the deprecated {Code} couldn't be counted")]
    private static partial void LogNotCounted(ILogger logger, Exception exception, string code);
}
