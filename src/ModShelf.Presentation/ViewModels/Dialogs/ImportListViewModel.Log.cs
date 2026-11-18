using Microsoft.Extensions.Logging;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// The import dialog's log messages.
/// </summary>
public sealed partial class ImportListViewModel
{
    [LoggerMessage(EventId = 980, Level = LogLevel.Warning, Message = "A list couldn't be imported")]
    private static partial void LogImportFailed(ILogger logger, Exception exception);
}
