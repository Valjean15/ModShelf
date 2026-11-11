using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Accounts;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// A site's card on the Accounts screen: the account linked there, with linking one each way the site offers, checking
/// it, and unlinking it. The site is asked about the account only when the user links or checks it.
/// </summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly AccountServices _services;
    private CancellationTokenSource? _linking;
    private bool _wanted;
    private bool _refused;
    private Uri? _browserPage;

    internal AccountViewModel(SourceAccountStatus status, AccountServices services)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        Source = status.Source;
        SiteName = status.DisplayName;
        WorksWithoutAccount = status.WorksWithoutAccount;
        AboutText = Text.Format(status.WorksWithoutAccount ? Strings.AccountOptional : Strings.AccountNeeded, SiteName);
        QuotaHeading = Text.Format(Strings.AccountQuotaHeading, SiteName);
        LinkMethods =
        [
            .. status.AuthKinds.Select((kind, index) => new LinkMethodViewModel(kind, index == 0, new AsyncRelayCommand(() => LinkAsync(kind), CanAct))),
        ];
        Picture = new GamePictureViewModel(services.Pictures);
        Picture.Show(CancellationToken.None);
        Show(status);
    }

    public SourceId Source { get; }

    /// <summary>
    /// Such as "Nexus Mods".
    /// </summary>
    public string SiteName { get; }

    /// <summary>
    /// Whether the site needs an account, or works without one.
    /// </summary>
    public string AboutText { get; }

    /// <summary>
    /// Whether ModShelf uses the site without an account too, as GameBanana; otherwise it needs one, as Nexus Mods.
    /// </summary>
    public bool WorksWithoutAccount { get; }

    /// <summary>
    /// Whether its row in Settings › Accounts shows the rest: the ways to link, checking, unlinking and the requests left.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText), nameof(ShowsLink))]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// "Details", or "Less" while the row shows them.
    /// </summary>
    public string DetailsText => IsExpanded ? Strings.AccountRowLess : Strings.AccountRowDetails;

    /// <summary>
    /// Whether the row offers Link…: without an account, until it's open.
    /// </summary>
    public bool ShowsLink => !IsLinked && !IsExpanded;

    /// <summary>
    /// Under the site's name: the account's name, with what's left of its requests; or that none is linked.
    /// </summary>
    public string SummaryText => !IsLinked ? Strings.AccountRowNotLinked
        : RequestsText is { } requests ? Text.Format(Strings.AccountRowSummary, AccountName, requests)
        : AccountName;

    /// <summary>
    /// Such as "19,998 of 20,000 requests left today", the allowance with least left, as the site last said; null while it
    /// hasn't, or without an account.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    public partial string? RequestsText { get; private set; }

    /// <summary>
    /// Why the account needs attention, such as that downloads wait for one; null when it doesn't.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsAttention))]
    public partial string? AttentionText { get; private set; }

    /// <summary>
    /// Whether Settings' badge counts it: downloads of the site failed for want of an account, or the site refused it.
    /// </summary>
    public bool NeedsAttention => AttentionText is not null;

    /// <summary>
    /// Each way the site offers to link an account, the one it offers first first. Linking replaces the account linked
    /// before.
    /// </summary>
    public IReadOnlyList<LinkMethodViewModel> LinkMethods { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnlinkCommand))]
    [NotifyPropertyChangedFor(nameof(ShowsLink), nameof(SummaryText))]
    public partial bool IsLinked { get; private set; }

    /// <summary>
    /// The account's name on the site; empty without an account.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial), nameof(Hue), nameof(SummaryText))]
    public partial string AccountName { get; private set; } = string.Empty;

    /// <summary>
    /// The account's picture on the site, beside its name; its initial on its colour while there's none.
    /// </summary>
    public GamePictureViewModel Picture { get; }

    /// <summary>
    /// The first letter of the account's name, in place of its picture while there's none.
    /// </summary>
    public string Initial => Covers.InitialOf(AccountName);

    /// <summary>
    /// The hue behind the initial, from the account's name, as in the menu.
    /// </summary>
    public int Hue => Covers.HueOf(AccountName);

    /// <summary>
    /// The account's membership on the site, such as "Premium"; null when the site gives none, or without an account.
    /// </summary>
    [ObservableProperty]
    public partial string? Tier { get; private set; }

    /// <summary>
    /// Such as "Linked on 10/29/2026"; or that no account is linked.
    /// </summary>
    [ObservableProperty]
    public partial string LinkedText { get; private set; } = string.Empty;

    /// <summary>
    /// Such as "API requests left, as Nexus Mods last said:", above <see cref="Quota"/>.
    /// </summary>
    public string QuotaHeading { get; }

    /// <summary>
    /// A line per allowance of the site's API, the longest first, such as "Per day: 19,998 of 20,000 left, until …";
    /// empty while the site hasn't said, or without an account.
    /// </summary>
    public ObservableCollection<string> Quota { get; } = [];

    [ObservableProperty]
    public partial bool HasQuota { get; private set; }

    /// <summary>
    /// Whether the card is linking, checking or unlinking, which it does one at a time.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnlinkCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>
    /// Whether linking waits for the user to approve ModShelf in the browser.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBrowserPageAgainCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopLinkingCommand))]
    public partial bool IsWaitingForBrowser { get; private set; }

    /// <summary>
    /// What to do in the browser while linking waits for it; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? BrowserNote { get; private set; }

    /// <summary>
    /// The page to approve ModShelf on, to open by hand, when the browser didn't open; null otherwise.
    /// </summary>
    [ObservableProperty]
    public partial string? BrowserPageText { get; private set; }

    /// <summary>
    /// What the last action did, such as "Linked as Jean.", or why it couldn't; null for nothing to say.
    /// </summary>
    [ObservableProperty]
    public partial string? Note { get; private set; }

    [ObservableProperty]
    public partial bool IsNoteAProblem { get; private set; }

    /// <summary>
    /// Shows the account as listed now. Runs on the UI thread.
    /// </summary>
    internal void Show(SourceAccountStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        Show(status.Account, status.Quota);
    }

    /// <summary>
    /// Says whether downloads of the site failed for want of an account (ADR 0094).
    /// </summary>
    internal void ShowWanted(bool wanted)
    {
        _wanted = wanted;
        ShowAttention();
    }

    /// <summary>
    /// Such as "Per day", for how long an allowance lasts.
    /// </summary>
    private static string PeriodName(TimeSpan period)
        => period == TimeSpan.FromDays(1) ? Strings.AccountQuotaPerDay
            : period == TimeSpan.FromHours(1) ? Strings.AccountQuotaPerHour
            : Text.Format(Strings.AccountQuotaPer, period.ToString("g", CultureInfo.CurrentCulture));

    /// <summary>
    /// Whether an action failed for a reason it says in words the user can act on, such as a keyring that's locked.
    /// </summary>
    private static bool IsFailureWithReason(Exception exception)
        => exception is InvalidOperationException or IOException or UnauthorizedAccessException or CryptographicException;

    private bool CanAct() => !IsBusy;

    /// <summary>
    /// Opens the row for the ways to link an account.
    /// </summary>
    [RelayCommand]
    private void Expand() => IsExpanded = true;

    /// <summary>
    /// Opens or closes the row's details.
    /// </summary>
    [RelayCommand]
    private void ToggleDetails() => IsExpanded = !IsExpanded;

    private void ShowAttention()
        => AttentionText = _refused ? Text.Format(Strings.AccountRowRefused, SiteName)
            : _wanted && IsLinked ? Text.Format(Strings.AccountRowWantedLinked, SiteName)
            : _wanted ? Strings.AccountRowWanted
            : null;

    private bool CanCheckOrUnlink() => IsLinked && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        foreach (LinkMethodViewModel method in LinkMethods)
        {
            method.Command.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Asks the site who the linked account is now, which also brings what's left of its API requests up to date.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckOrUnlink))]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        Begin();
        await UserCommands.RunInPlaceAsync(
            async () =>
            {
                try
                {
                    LinkedAccount account = await _services.Accounts.CheckAsync(Source, cancellationToken);
                    _refused = false;
                    await ShowLatestAsync(account);
                    Say(Text.Format(Strings.AccountChecked, Dates.Time(_services.TimeProvider.GetUtcNow(), _services.TimeProvider)), false);
                }
                catch (KeyNotFoundException)
                {
                    // Unlinked elsewhere, such as from the command line.
                    await ShowLatestAsync(null);
                    Say(Text.Format(Strings.AccountNoLongerLinked, SiteName), true);
                }
            },
            exception => exception switch
            {
                AuthenticationFailedException => Refused(),
                _ when DescribeSiteProblem(exception) is { } problem => problem,
                _ when IsFailureWithReason(exception) => exception.Message,
                _ => null,
            },
            problem => Say(problem, true),
            exception => LogCheckFailed(_services.Logger, Source, exception),
            cancellationToken);
        IsBusy = false;
    }

    /// <summary>
    /// Removes the account and its stored credentials, once the user confirms.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckOrUnlink))]
    private async Task UnlinkAsync(CancellationToken cancellationToken)
    {
        bool confirmed = await _services.Dialogs.ConfirmAsync(
            Text.Format(Strings.AccountUnlinkTitle, SiteName),
            Text.Format(Strings.AccountUnlinkMessage, SiteName, AccountName),
            Strings.AccountUnlink,
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        Begin();
        await UserCommands.RunInPlaceAsync(
            async () =>
            {
                await _services.Accounts.UnlinkAsync(Source, cancellationToken);
                await ShowLatestAsync(null);
                Say(Strings.AccountUnlinked, false);
            },
            exception => IsFailureWithReason(exception) ? Text.Format(Strings.AccountUnlinkProblem, exception.Message) : null,
            problem => Say(problem, true),
            exception => LogUnlinkFailed(_services.Logger, Source, exception),
            cancellationToken);
        IsBusy = false;
    }

    /// <summary>
    /// Notes that the site refused the account, which Settings' badge then counts, and says so.
    /// </summary>
    private string Refused()
    {
        _refused = true;
        ShowAttention();
        return Text.Format(Strings.AccountCheckRefused, SiteName);
    }

    private void Begin()
    {
        IsBusy = true;
        Note = null;
        IsNoteAProblem = false;
    }

    private void Say(string note, bool isProblem)
    {
        Note = note;
        IsNoteAProblem = isProblem;
    }

    /// <summary>
    /// What to say when the site couldn't be asked, or couldn't answer; null for another problem.
    /// </summary>
    private string? DescribeSiteProblem(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => Text.Format(Strings.AccountSlowDown, SiteName),

        // It answered, so it was reached; it couldn't do what was asked.
        HttpRequestException { StatusCode: not null } failed => Text.Format(Strings.AccountSiteError, SiteName, failed.Message),
        HttpRequestException => Text.Format(Strings.AccountUnreachable, SiteName),
        _ when SiteProblems.IsTimeout(exception) => Text.Format(Strings.AccountUnreachable, SiteName),
        _ => null,
    };

    private void Show(LinkedAccount? account, ApiQuota? quota)
    {
        IsLinked = account is not null;
        foreach (LinkMethodViewModel method in LinkMethods)
        {
            method.IsMain = method.IsFirst && !IsLinked;
        }

        AccountName = account?.Profile.DisplayName ?? string.Empty;
        Tier = account?.Profile.Tier;
        Picture.Place(account?.Profile.AvatarUrl, CancellationToken.None);
        if (account is null)
        {
            _refused = false;
        }

        ShowAttention();
        LinkedText = account is null
            ? Strings.AccountNotLinked
            : Text.Format(Strings.AccountLinkedOn, Dates.Day(account.LinkedAt, _services.TimeProvider));

        Quota.Clear();
        foreach (QuotaAllowance allowance in account is null ? [] : quota?.Allowances ?? [])
        {
            Quota.Add(Text.Format(
                Strings.AccountQuotaLine,
                PeriodName(allowance.Period),
                allowance.Remaining,
                allowance.Limit,
                Dates.DayAndTime(allowance.ResetsAt, _services.TimeProvider)));
        }

        HasQuota = Quota.Count > 0;
        RequestsText = (account is null ? [] : quota?.Allowances ?? [])
            .OrderBy(allowance => allowance.Limit > 0 ? (double)allowance.Remaining / allowance.Limit : 1)
            .Select(allowance => Text.Format(
                allowance.Period == TimeSpan.FromDays(1) ? Strings.AccountRowRequestsToday
                    : allowance.Period == TimeSpan.FromHours(1) ? Strings.AccountRowRequestsThisHour
                    : Strings.AccountRowRequests,
                allowance.Remaining,
                allowance.Limit))
            .FirstOrDefault();
    }

    /// <summary>
    /// Shows the account as listed now, with what's left of the site's API requests. When the accounts can't be listed,
    /// shows <paramref name="account"/>, as the action left it, and logs why.
    /// </summary>
    private async Task ShowLatestAsync(LinkedAccount? account)
    {
        try
        {
            IReadOnlyList<SourceAccountStatus> statuses = await _services.Accounts.ListAsync(CancellationToken.None);
            if (statuses.FirstOrDefault(status => status.Source == Source) is { } status)
            {
                Show(status);
                return;
            }
        }
        catch (Exception exception)
        {
            LogListFailed(_services.Logger, Source, exception);
        }

        Show(account, null);
    }
}
