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
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.ViewModels;

/// <summary>
/// A site's card on the Accounts screen: the account linked there, with linking one each way the site offers, checking
/// it, and unlinking it. The site is asked about the account only when the user links or checks it.
/// </summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly AccountServices _services;
    private CancellationTokenSource? _linking;
    private Uri? _browserPage;

    internal AccountViewModel(SourceAccountStatus status, AccountServices services)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        Source = status.Source;
        SiteName = status.DisplayName;
        AboutText = Text.Format(status.WorksWithoutAccount ? Strings.AccountOptional : Strings.AccountNeeded, SiteName);
        QuotaHeading = Text.Format(Strings.AccountQuotaHeading, SiteName);
        LinkMethods =
        [
            .. status.AuthKinds.Select((kind, index) => new LinkMethodViewModel(kind, index == 0, new AsyncRelayCommand(() => LinkAsync(kind), CanAct))),
        ];
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
    /// Each way the site offers to link an account, the one it offers first first. Linking replaces the account linked
    /// before.
    /// </summary>
    public IReadOnlyList<LinkMethodViewModel> LinkMethods { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(UnlinkCommand))]
    public partial bool IsLinked { get; private set; }

    /// <summary>
    /// The account's name on the site; empty without an account.
    /// </summary>
    [ObservableProperty]
    public partial string AccountName { get; private set; } = string.Empty;

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

    private static bool IsTimeout(Exception exception) => exception is TaskCanceledException { InnerException: TimeoutException };

    private bool CanAct() => !IsBusy;

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
        try
        {
            LinkedAccount account = await _services.Accounts.CheckAsync(Source, cancellationToken);
            await ShowLatestAsync(account);
            Say(Text.Format(Strings.AccountChecked, Dates.Time(_services.TimeProvider.GetUtcNow(), _services.TimeProvider)), false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (KeyNotFoundException)
        {
            // Unlinked elsewhere, such as from the command line.
            await ShowLatestAsync(null);
            Say(Text.Format(Strings.AccountNoLongerLinked, SiteName), true);
        }
        catch (AuthenticationFailedException)
        {
            Say(Text.Format(Strings.AccountCheckRefused, SiteName), true);
        }
        catch (Exception exception) when (DescribeSiteProblem(exception) is { } problem)
        {
            Say(problem, true);
        }
        catch (Exception exception) when (IsFailureWithReason(exception))
        {
            Say(exception.Message, true);
        }
        catch (Exception exception)
        {
            LogCheckFailed(_services.Logger, Source, exception);
            Say(Strings.SomethingWentWrong, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Links an account the way given, replacing the one linked before. Closing a dialog, or cancelling while the browser
    /// is waited for, links nothing.
    /// </summary>
    private async Task LinkAsync(AuthKind kind)
    {
        using CancellationTokenSource linking = new();
        _linking = linking;
        Begin();
        try
        {
            DialogAuthPrompt prompt = new(
                SiteName,
                _services.Dialogs,
                (page, opened) => _services.UiThread.Post(() =>
                {
                    // A step of an earlier linking, shown too late, isn't shown.
                    if (_linking == linking)
                    {
                        ShowBrowserStep(page, opened);
                    }
                }));
            LinkedAccount account = await _services.Accounts.LinkAsync(Source, kind, prompt, linking.Token);
            await ShowLatestAsync(account);
            Say(Text.Format(Strings.AccountLinked, account.Profile.DisplayName), false);
        }
        catch (Exception exception) when (IsTimeout(exception))
        {
            Say(Text.Format(Strings.AccountUnreachable, SiteName), true);
        }
        catch (OperationCanceledException)
        {
            // The user closed a dialog, or stopped waiting for the browser.
        }
        catch (AuthenticationFailedException)
        {
            Say(
                Text.Format(
                    kind switch
                    {
                        AuthKind.ApiKey => Strings.AccountApiKeyRefused,
                        AuthKind.EmailCode => Strings.AccountCodeRefused,
                        _ => Strings.AccountBrowserRefused,
                    },
                    SiteName),
                true);
        }
        catch (Exception exception) when (DescribeSiteProblem(exception) is { } problem)
        {
            Say(problem, true);
        }
        catch (ArgumentException exception)
        {
            // Such as a blank API key.
            Say(Text.Format(Strings.AccountNothingLinked, LinkProblems.MessageForUser(exception)), true);
        }
        catch (Exception exception) when (IsFailureWithReason(exception))
        {
            Say(Text.Format(Strings.AccountNothingLinked, exception.Message), true);
        }
        catch (Exception exception)
        {
            LogLinkFailed(_services.Logger, Source, exception);
            Say(Strings.SomethingWentWrong, true);
        }
        finally
        {
            _linking = null;
            _browserPage = null;
            IsWaitingForBrowser = false;
            BrowserNote = null;
            BrowserPageText = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the page to approve ModShelf on again, such as after the user closed it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsWaitingForBrowser))]
    private async Task OpenBrowserPageAgainAsync(CancellationToken cancellationToken)
    {
        if (_browserPage is not { } page)
        {
            return;
        }

        bool opened = await _services.Browser.TryOpenAsync(page, cancellationToken);
        if (_browserPage == page)
        {
            ShowBrowserStep(page, opened);
        }
    }

    /// <summary>
    /// Stops waiting for the browser; nothing is linked.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsWaitingForBrowser))]
    private void StopLinking() => _linking?.Cancel();

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
        try
        {
            await _services.Accounts.UnlinkAsync(Source, cancellationToken);
            await ShowLatestAsync(null);
            Say(Strings.AccountUnlinked, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception) when (IsFailureWithReason(exception))
        {
            Say(Text.Format(Strings.AccountUnlinkProblem, exception.Message), true);
        }
        catch (Exception exception)
        {
            LogUnlinkFailed(_services.Logger, Source, exception);
            Say(Strings.SomethingWentWrong, true);
        }
        finally
        {
            IsBusy = false;
        }
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
        _ when IsTimeout(exception) => Text.Format(Strings.AccountUnreachable, SiteName),
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

    /// <summary>
    /// Shows that linking waits for the user to approve ModShelf on <paramref name="page"/>. Runs on the UI thread.
    /// </summary>
    private void ShowBrowserStep(Uri page, bool opened)
    {
        _browserPage = page;
        IsWaitingForBrowser = true;
        BrowserNote = Text.Format(opened ? Strings.AccountWaitingForBrowser : Strings.AccountBrowserDidntOpen, SiteName);
        BrowserPageText = opened ? null : page.AbsoluteUri;
    }
}
