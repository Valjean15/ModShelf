using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// The CurseForge source's log messages.
/// </summary>
public sealed partial class CurseForgeSource
{
    [LoggerMessage(EventId = 2900, Level = LogLevel.Information, Message = "CurseForge's mod {ModId} has more files than ModShelf reads; the newest {Files} are read")]
    private static partial void LogFilesCut(ILogger logger, long modId, int files);
}
