using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// A card per site that can link an account: the account linked there, linking one, checking it and unlinking it. It
/// says when no keyring protects the credentials.
/// </summary>
public sealed partial class AccountsViewModel : ScreenViewModel
{
    private readonly IAccountService _accounts;
    private readonly IUiThread _uiThread;
    private readonly ILogger<AccountsViewModel> _logger;
    private readonly AccountServices _services;

    public AccountsViewModel(
        IAccountService accounts,
        ISecretStore secrets,
        IBrowserLauncher browser,
        IDialogService dialogs,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ILogger<AccountsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(logger);

        _accounts = accounts;
        _uiThread = uiThread;
        _logger = logger;
        _services = new AccountServices(accounts, browser, dialogs, uiThread, timeProvider, logger);
        IsStoreUnprotected = !secrets.IsProtectedByOs;
    }

    public override Screen Screen => Screen.Accounts;

    public override string Title => Strings.AccountsTitle;

    /// <summary>
    /// Each site that can link an account, in the order the sources are registered.
    /// </summary>
    public ObservableCollection<AccountViewModel> Sources { get; } = [];

    /// <summary>
    /// Whether the accounts are still being listed.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; } = true;

    /// <summary>
    /// What the screen says without cards: that no site supports accounts, or that they couldn't be listed. Null with
    /// cards, or while listing.
    /// </summary>
    [ObservableProperty]
    public partial string? EmptyText { get; private set; }

    [ObservableProperty]
    public partial bool IsEmptyTextAProblem { get; private set; }

    /// <summary>
    /// Whether credentials are kept in a file encrypted for this computer, which other programs running as the user can
    /// read, because no keyring is running.
    /// </summary>
    public bool IsStoreUnprotected { get; }

    /// <summary>
    /// Lists the sites that can link an account, with the account linked on each. The sites aren't asked anything.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
        => UserCommands.RunInPlaceAsync(
            async () =>
            {
                IReadOnlyList<SourceAccountStatus> statuses = await _accounts.ListAsync(cancellationToken);
                _uiThread.Post(() =>
                {
                    foreach (SourceAccountStatus status in statuses)
                    {
                        Sources.Add(new AccountViewModel(status, _services));
                    }

                    EmptyText = statuses.Count == 0 ? Strings.AccountsNone : null;
                    IsLoading = false;
                });
            },
            _ => null,
            problem => _uiThread.Post(() =>
            {
                EmptyText = problem;
                IsEmptyTextAProblem = true;
                IsLoading = false;
            }),
            exception => LogListFailed(_logger, exception),
            cancellationToken);
}
