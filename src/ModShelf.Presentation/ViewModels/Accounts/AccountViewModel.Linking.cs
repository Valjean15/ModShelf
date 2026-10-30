using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Accounts;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.ViewModels.Accounts;

/// <summary>
/// Linking an account, with the steps that need the browser.
/// </summary>
public sealed partial class AccountViewModel
{
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
        catch (Exception exception) when (SiteProblems.IsTimeout(exception))
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
            Say(Text.Format(Strings.AccountNothingLinked, exception.MessageForUser()), true);
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
