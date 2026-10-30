using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Downloads;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// What each <see cref="DownloadJobViewModel"/> on the Downloads screen uses, handed over by the screen as one.
/// </summary>
internal sealed class DownloadJobServices(
    IDownloadService downloads,
    ILinkDownloads links,
    IBrowserLauncher browser,
    IDialogService dialogs,
    TimeProvider timeProvider,
    ILogger logger)
{
    public IDownloadService Downloads { get; } = downloads ?? throw new ArgumentNullException(nameof(downloads));

    public ILinkDownloads Links { get; } = links ?? throw new ArgumentNullException(nameof(links));

    public IBrowserLauncher Browser { get; } = browser ?? throw new ArgumentNullException(nameof(browser));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
