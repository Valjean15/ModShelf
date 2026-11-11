using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Downloads;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// The sites' accounts, Settings' first category (ADR 0094): a row per site that can link an account, grouped by those
/// that need one and those that work without one, each opening for linking one, checking it and unlinking it; then the
/// sites that need none, with what ModShelf does there. It says when no keyring protects the credentials. Each time it's
/// shown, it lists the accounts again, as one linked from the command line meanwhile. Its badge counts the accounts that
/// need attention: downloads of the site failed for want of one, or the site refused it.
/// </summary>
public sealed partial class AccountsViewModel : ScreenViewModel
{
    private readonly IAccountService _accounts;
    private readonly IUiThread _uiThread;
    private readonly SitesWantingAccounts _wantingAccounts;
    private readonly ILogger<AccountsViewModel> _logger;
    private readonly AccountServices _services;

    public AccountsViewModel(
        IAccountService accounts,
        ISecretStore secrets,
        IBrowserLauncher browser,
        IDialogService dialogs,
        IUiThread uiThread,
        TimeProvider timeProvider,
        ModPictures pictures,
        ISourceRegistry sources,
        SitesWantingAccounts wantingAccounts,
        ILogger<AccountsViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(uiThread);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(wantingAccounts);
        ArgumentNullException.ThrowIfNull(logger);

        _accounts = accounts;
        _uiThread = uiThread;
        _wantingAccounts = wantingAccounts;
        _logger = logger;
        _services = new AccountServices(accounts, browser, dialogs, uiThread, timeProvider, pictures, logger);
        IsStoreUnprotected = !secrets.IsProtectedByOs;
        NoneNeeded = [.. sources.All<IModSource>().Where(source => source is not IAccountLinker).Select(WithoutAccount)];
        wantingAccounts.Changed += (_, _) => _uiThread.Post(ShowWanted);
    }

    public override Screen Screen => Screen.Accounts;

    public override string Title => Strings.AccountsTitle;

    /// <summary>
    /// Each site that can link an account, in the order the sources are registered.
    /// </summary>
    public ObservableCollection<AccountViewModel> Sources { get; } = [];

    /// <summary>
    /// The sites that need an account, such as Nexus Mods and mod.io.
    /// </summary>
    public ObservableCollection<AccountViewModel> NeedOne { get; } = [];

    /// <summary>
    /// The sites that can link an account and work without one, such as GameBanana.
    /// </summary>
    public ObservableCollection<AccountViewModel> Optional { get; } = [];

    /// <summary>
    /// The sites ModShelf uses with no account, such as DEG Mods and web links.
    /// </summary>
    public IReadOnlyList<SiteWithoutAccountViewModel> NoneNeeded { get; }

    [ObservableProperty]
    public partial bool HasNeedOne { get; private set; }

    [ObservableProperty]
    public partial bool HasOptional { get; private set; }

    public bool HasNoneNeeded => NoneNeeded.Count > 0;

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
    /// The last listing of the accounts as the screen was shown, for tests to wait on.
    /// </summary>
    internal Task Refreshing { get; private set; } = Task.CompletedTask;

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
                        AccountViewModel row = new(status, _services);
                        row.PropertyChanged += (_, changed) =>
                        {
                            if (changed.PropertyName == nameof(AccountViewModel.NeedsAttention))
                            {
                                ShowBadge();
                            }
                        };
                        Sources.Add(row);
                        (row.WorksWithoutAccount ? Optional : NeedOne).Add(row);
                    }

                    HasNeedOne = NeedOne.Count > 0;
                    HasOptional = Optional.Count > 0;
                    EmptyText = statuses.Count == 0 && NoneNeeded.Count == 0 ? Strings.AccountsNone : null;
                    IsLoading = false;
                    ShowWanted();
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

    /// <summary>
    /// Lists the accounts again, once the screen has listed them, and shows each card's but those busy linking, checking
    /// or unlinking. The sites aren't asked anything.
    /// </summary>
    public override void OnShown() => Refreshing = RefreshAsync();

    /// <summary>
    /// What ModShelf does on a site with no account: such as "ModShelf searches its mods, downloads its files and checks
    /// them for updates, with no account."
    /// </summary>
    private static SiteWithoutAccountViewModel WithoutAccount(IModSource source)
    {
        List<string> does = [];
        if (source is IModSearch)
        {
            does.Add(Strings.AccountWithoutSearches);
        }

        if (source is IDownloadResolver)
        {
            does.Add(Strings.AccountWithoutDownloads);
        }

        if (source is IModCatalog or IUpdateFeed)
        {
            does.Add(Strings.AccountWithoutChecks);
        }

        return new SiteWithoutAccountViewModel(source.DisplayName, Text.Format(Strings.AccountWithout, Text.List(does)));
    }

    /// <summary>
    /// Shows on each row whether downloads of its site failed for want of an account.
    /// </summary>
    private void ShowWanted()
    {
        IReadOnlySet<SourceId> wanted = _wantingAccounts.Sites;
        foreach (AccountViewModel row in Sources)
        {
            row.ShowWanted(wanted.Contains(row.Source));
        }

        ShowBadge();
    }

    private void ShowBadge() => Badge = Sources.Count(row => row.NeedsAttention);

    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IReadOnlyList<SourceAccountStatus> statuses = await _accounts.ListAsync(CancellationToken.None);
            _uiThread.Post(() =>
            {
                foreach (SourceAccountStatus status in statuses)
                {
                    if (Sources.FirstOrDefault(card => card.Source == status.Source) is { IsBusy: false } card)
                    {
                        card.Show(status);
                    }
                }
            });
        }
        catch (Exception exception)
        {
            // The cards show the accounts as they were.
            LogListFailed(_logger, exception);
        }
    }
}
