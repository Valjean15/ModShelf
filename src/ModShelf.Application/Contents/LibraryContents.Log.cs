using Microsoft.Extensions.Logging;

namespace ModShelf.Application.Contents;

/// <summary>
/// The log messages of showing archives' contents.
/// </summary>
internal sealed partial class LibraryContents
{
    [LoggerMessage(EventId = 2800, Level = LogLevel.Debug, Message = "Listed {Files} files in {Folders} folders of {Archive}")]
    private static partial void LogListed(ILogger logger, string archive, int files, int folders);

    [LoggerMessage(EventId = 2801, Level = LogLevel.Information, Message = "{Archive} couldn't be read: {Trouble}")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, string archive, ArchiveTrouble trouble);
}
