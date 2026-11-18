using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Archives;

/// <summary>
/// Importing archives' log messages, which name archives by their file names alone.
/// </summary>
internal sealed partial class ArchiveImport
{
    [LoggerMessage(EventId = 2400, Level = LogLevel.Information, Message = "Found {Archives} archives to import for {Source}/{Game}, {Named} of them named by a note or their name")]
    private static partial void LogScanned(ILogger logger, int archives, int named, string source, string game);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Information, Message = "Identified {Archives} archives in {Requests} requests: {Identified} to import, {InLibrary} in the library, {Duplicates} duplicates, {Unknown} unknown")]
    private static partial void LogIdentified(ILogger logger, int archives, int identified, int inLibrary, int duplicates, int unknown, int requests);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Information, Message = "Imported {Imported} archives into {Source}/{Game}, {ModsAdded} mods new to the library; {InLibrary} were in it already, {Problems} couldn't be imported")]
    private static partial void LogImported(ILogger logger, int imported, int modsAdded, int inLibrary, int problems, string source, string game);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Information, Message = "Copied {FileName} into the library as a file of mod {ModId} of {Game}")]
    private static partial void LogCopied(ILogger logger, string fileName, string modId, string game);

    [LoggerMessage(EventId = 2404, Level = LogLevel.Warning, Message = "Couldn't read {FileName} to identify it")]
    private static partial void LogCantRead(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 2405, Level = LogLevel.Warning, Message = "Couldn't read the note {FileName}, so its archive is identified otherwise")]
    private static partial void LogCantReadNote(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(EventId = 2406, Level = LogLevel.Warning, Message = "Couldn't ask {Source} about archives of {Game}")]
    private static partial void LogCantAsk(ILogger logger, Exception exception, string source, string game);

    [LoggerMessage(EventId = 2407, Level = LogLevel.Warning, Message = "Couldn't copy {FileName} into the library")]
    private static partial void LogCantCopy(ILogger logger, Exception exception, string fileName);
}
