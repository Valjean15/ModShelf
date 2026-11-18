using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Lists;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Dialogs;

/// <summary>
/// Something a collection needs from outside its mods' site, in the import dialog: its name, and its page to open in
/// the browser, for the user to get it. ModShelf doesn't download it.
/// </summary>
public sealed partial class ImportListElsewhereViewModel
{
    private readonly ListedElsewhere _thing;
    private readonly IBrowserLauncher _browser;
    private readonly IDialogService _dialogs;

    internal ImportListElsewhereViewModel(ListedElsewhere thing, IBrowserLauncher browser, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(thing);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(dialogs);

        _thing = thing;
        _browser = browser;
        _dialogs = dialogs;
    }

    public string Name => _thing.Name;

    /// <summary>
    /// Whether the collection marks it optional.
    /// </summary>
    public bool IsOptional => _thing.Optional;

    /// <summary>
    /// Whether it has a page to open.
    /// </summary>
    public bool HasPage => _thing.PageUrl is not null;

    /// <summary>
    /// Opens its page in the browser. When no browser opens, the page's address is shown instead.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasPage))]
    private Task OpenPageAsync(CancellationToken cancellationToken)
        => _thing.PageUrl is { } page
            ? Pages.OpenAsync(page, _browser, _dialogs, Strings.OpenPageByHand, cancellationToken)
            : Task.CompletedTask;
}
