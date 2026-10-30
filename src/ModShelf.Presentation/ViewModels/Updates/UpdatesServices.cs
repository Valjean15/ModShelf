using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Library;
using ModShelf.Application.Sources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Updates;

/// <summary>
/// What each update on the Updates screen uses, handed over by the screen as one.
/// </summary>
internal sealed class UpdatesServices(
    ILibraryDownloads downloads,
    ISourceRegistry sources,
    IModLookup lookup,
    IDialogService dialogs,
    TimeProvider timeProvider,
    ILogger logger)
{
    public ILibraryDownloads Downloads { get; } = downloads ?? throw new ArgumentNullException(nameof(downloads));

    public ISourceRegistry Sources { get; } = sources ?? throw new ArgumentNullException(nameof(sources));

    public IModLookup Lookup { get; } = lookup ?? throw new ArgumentNullException(nameof(lookup));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
