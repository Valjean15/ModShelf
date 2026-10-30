using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// What each site's card on the Accounts screen uses, handed over by the screen as one.
/// </summary>
internal sealed class AccountServices(
    IAccountService accounts,
    IBrowserLauncher browser,
    IDialogService dialogs,
    IUiThread uiThread,
    TimeProvider timeProvider,
    ILogger logger)
{
    public IAccountService Accounts { get; } = accounts ?? throw new ArgumentNullException(nameof(accounts));

    public IBrowserLauncher Browser { get; } = browser ?? throw new ArgumentNullException(nameof(browser));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public IUiThread UiThread { get; } = uiThread ?? throw new ArgumentNullException(nameof(uiThread));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
