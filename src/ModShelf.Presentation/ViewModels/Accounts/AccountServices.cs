using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;

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
    ModPictures pictures,
    AccountMenuViewModel menu,
    ILogger logger)
{
    public IAccountService Accounts { get; } = accounts ?? throw new ArgumentNullException(nameof(accounts));

    public IBrowserLauncher Browser { get; } = browser ?? throw new ArgumentNullException(nameof(browser));

    public IDialogService Dialogs { get; } = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public IUiThread UiThread { get; } = uiThread ?? throw new ArgumentNullException(nameof(uiThread));

    public TimeProvider TimeProvider { get; } = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Where the accounts' pictures are read.
    /// </summary>
    public ModPictures Pictures { get; } = pictures ?? throw new ArgumentNullException(nameof(pictures));

    /// <summary>
    /// The accounts at the bottom of the menu, which each card tells of its account as it shows it.
    /// </summary>
    public AccountMenuViewModel Menu { get; } = menu ?? throw new ArgumentNullException(nameof(menu));

    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
