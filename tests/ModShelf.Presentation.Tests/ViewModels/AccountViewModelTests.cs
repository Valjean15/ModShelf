using System.Net;

using ModShelf.Application.Accounts;
using ModShelf.Application.Accounts.Ports;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.ViewModels;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Quota;

namespace ModShelf.Presentation.Tests.ViewModels;

/// <summary>
/// A site's card on the Accounts screen: linking an account each way, checking it and unlinking it.
/// </summary>
public sealed class AccountViewModelTests
{
    private const string NexusRefusedTheKey = "Nexus Mods refused the API key. Check it, then try again.";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly AccountsHarness _harness = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// What en-US puts between a time and AM or PM, which depends on the system's culture data: a space, or a narrow one.
    /// </summary>
    private static string AmPm => new DateTime(2026, 1, 1, 13, 0, 0).ToString("t", System.Globalization.CultureInfo.GetCultureInfo("en-US"))[4..^2];

    public static TheoryData<string, string, bool> LinkProblems() => new()
    {
        { "refused", NexusRefusedTheKey, false },
        { "slow down", "Nexus Mods asked ModShelf to slow down. Try again in a while.", false },
        { "error", "Nexus Mods answered with an error: Bad gateway. Try again later.", false },
        { "unreachable", "Couldn't reach Nexus Mods. Check your connection, then try again.", false },
        { "timeout", "Couldn't reach Nexus Mods. Check your connection, then try again.", false },
        { "blank", "No API key was entered. Nothing was linked.", false },
        { "keyring", "The Secret Service couldn't do that: the keyring is locked. Nothing was linked.", false },
        { "unexpected", Strings.SomethingWentWrong, true },
    };

    public static TheoryData<string, string, bool> CheckProblems() => new()
    {
        { "refused", "Nexus Mods refused the account's credentials, or they can't be used any more. Link it again.", false },
        { "slow down", "Nexus Mods asked ModShelf to slow down. Try again in a while.", false },
        { "error", "Nexus Mods answered with an error: Bad gateway. Try again later.", false },
        { "unreachable", "Couldn't reach Nexus Mods. Check your connection, then try again.", false },
        { "timeout", "Couldn't reach Nexus Mods. Check your connection, then try again.", false },
        { "keyring", "The Secret Service couldn't do that: the keyring is locked.", false },
        { "unexpected", Strings.SomethingWentWrong, true },
    };

    [Fact]
    public async Task AnApiKeyPastedInADialogLinksTheAccount()
    {
        await _harness.StartAsync();
        string? asked = null;
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            asked = await prompt.AskApiKeyAsync(source, AccountsHarness.KeyPage, token);
            return AccountsHarness.Account(source, kind, tier: "Supporter");
        };
        _harness.Dialogs.Answers.Enqueue("the-key");
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.LinkMethods[0].Command.ExecuteAsync(null);

        Assert.Equal("the-key", asked);
        Assert.All(nexus.LinkMethods, method => Assert.False(method.IsMain));
        TextQuestion question = Assert.Single(_harness.Dialogs.Questions);
        Assert.Equal(
            new TextQuestion("Link Nexus Mods", "Paste your personal API key for Nexus Mods. You'll find it on your account's API key page.", "API key", "Link")
            {
                IsSecret = true,
                HelpPage = AccountsHarness.KeyPage,
                HelpPageLabel = "Open the API key page",
            },
            question);
        Assert.True(nexus.IsLinked);
        Assert.Equal(("Jean", "Supporter"), (nexus.AccountName, nexus.Tier));
        Assert.Equal(("Linked as Jean.", false), (nexus.Note, nexus.IsNoteAProblem));
        Assert.False(nexus.IsBusy);
        Assert.Equal(["list", "link nexus ApiKey", "list"], _harness.Accounts.Calls);
    }

    [Fact]
    public async Task ASiteWithoutAKeyPageAsksForTheKeyAlone()
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            await prompt.AskApiKeyAsync(source, null, token);
            return AccountsHarness.Account(source, kind);
        };
        _harness.Dialogs.Answers.Enqueue("the-key");

        await _harness.Card(TestData.Nexus).LinkMethods[0].Command.ExecuteAsync(null);

        TextQuestion question = Assert.Single(_harness.Dialogs.Questions);
        Assert.Equal("Paste your personal API key for Nexus Mods.", question.Message);
        Assert.True(question.IsSecret);
        Assert.Null(question.HelpPage);
        Assert.Null(question.HelpPageLabel);
    }

    [Fact]
    public async Task ClosingTheDialogLinksNothingAndSaysNothing()
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            await prompt.AskApiKeyAsync(source, AccountsHarness.KeyPage, token);
            return AccountsHarness.Account(source, kind);
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.LinkMethods[0].Command.ExecuteAsync(null);

        Assert.False(nexus.IsLinked);
        Assert.Null(nexus.Note);
        Assert.False(nexus.IsBusy);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Theory]
    [MemberData(nameof(LinkProblems))]
    public async Task ALinkThatFailsSaysWhatToDo(string problem, string note, bool logged)
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = (_, _, _, _) => throw Failure(problem);
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.LinkMethods[0].Command.ExecuteAsync(null);

        Assert.False(nexus.IsLinked);
        Assert.Equal((note, true), (nexus.Note, nexus.IsNoteAProblem));
        Assert.False(nexus.IsBusy);
        Assert.Equal(logged, _harness.Logger.Entries.Any(entry => entry.StartsWith("Error: Linking an account on nexus failed", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(AuthKind.ApiKey, NexusRefusedTheKey)]
    [InlineData(AuthKind.OAuthPkce, "Nexus Mods didn't let ModShelf in. Try again, and approve ModShelf in the browser.")]
    [InlineData(AuthKind.EmailCode, "mod.io refused the code. Check it, or ask for a new one, then try again.")]
    public async Task ARefusalSaysWhatWasRefused(AuthKind kind, string note)
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = (_, _, _, _) => throw new AuthenticationFailedException("Refused.");
        AccountViewModel card = _harness.Screen.Sources.First(card => card.LinkMethods.Any(method => method.Kind == kind));

        await card.LinkMethods.Single(method => method.Kind == kind).Command.ExecuteAsync(null);

        Assert.Equal(note, card.Note);
    }

    [Fact]
    public async Task AnEmailedCodeAsksForTheAddressThenTheCode()
    {
        await _harness.StartAsync();
        List<string> answers = [];
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            answers.Add(await prompt.AskEmailAsync(source, token));
            answers.Add(await prompt.AskEmailCodeAsync(source, "j***@example.com", token));
            return AccountsHarness.Account(source, kind, name: "jean", tier: null);
        };
        _harness.Dialogs.Answers.Enqueue("jean@example.com");
        _harness.Dialogs.Answers.Enqueue("123456");
        AccountViewModel modIo = _harness.Card(AccountsHarness.ModIo);

        await modIo.LinkMethods[0].Command.ExecuteAsync(null);

        Assert.Equal(["jean@example.com", "123456"], answers);
        Assert.Equal(
            [
                new TextQuestion("Link mod.io", "Enter the email address of your account on mod.io. mod.io sends a code to it.", "name@example.com", "Send code"),
                new TextQuestion("Link mod.io", "Enter the code mod.io sent to j***@example.com.", "Code", "Link"),
            ],
            _harness.Dialogs.Questions);
        Assert.True(modIo.IsLinked);
        Assert.Null(modIo.Tier);
        Assert.Equal("Linked as jean.", modIo.Note);
    }

    [Fact]
    public async Task ACodeWithoutAnAddressSaysItWasEmailed()
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            await prompt.AskEmailCodeAsync(source, null, token);
            return AccountsHarness.Account(source, kind);
        };
        _harness.Dialogs.Answers.Enqueue("123456");

        await _harness.Card(AccountsHarness.ModIo).LinkMethods[0].Command.ExecuteAsync(null);

        Assert.Equal("Enter the code mod.io emailed you.", Assert.Single(_harness.Dialogs.Questions).Message);
    }

    [Fact]
    public async Task LinkingInTheBrowserWaitsForTheUserToApprove()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        TaskCompletionSource approved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            prompt.ShowBrowserStep(source, AccountsHarness.ApprovalPage, browserOpened: true);
            await approved.Task.WaitAsync(token);
            return AccountsHarness.Account(source, kind, name: "Robin");
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        LinkMethodViewModel browser = nexus.LinkMethods[1];

        Task linking = browser.Command.ExecuteAsync(null);
        Assert.True(nexus.IsWaitingForBrowser);
        Assert.Equal("Sign in to Nexus Mods in the page that opened in your browser, and approve ModShelf. Linking finishes here once you have.", nexus.BrowserNote);
        Assert.Null(nexus.BrowserPageText);
        Assert.True(nexus.IsBusy);
        Assert.All(nexus.LinkMethods, method => Assert.False(method.Command.CanExecute(null)));
        Assert.False(nexus.CheckCommand.CanExecute(null));
        Assert.False(nexus.UnlinkCommand.CanExecute(null));
        Assert.True(nexus.OpenBrowserPageAgainCommand.CanExecute(null));
        Assert.True(nexus.StopLinkingCommand.CanExecute(null));

        await nexus.OpenBrowserPageAgainCommand.ExecuteAsync(null);
        approved.SetResult();
        await linking.WaitAsync(Patience, Cancellation);

        Assert.Equal([AccountsHarness.ApprovalPage], _harness.Browser.Opened);
        Assert.False(nexus.IsWaitingForBrowser);
        Assert.Null(nexus.BrowserNote);
        Assert.False(nexus.OpenBrowserPageAgainCommand.CanExecute(null));
        Assert.False(nexus.StopLinkingCommand.CanExecute(null));
        Assert.Equal(("Robin", "Linked as Robin."), (nexus.AccountName, nexus.Note));
        Assert.All(nexus.LinkMethods, method => Assert.True(method.Command.CanExecute(null)));
    }

    [Fact]
    public async Task CancellingWhileTheBrowserIsWaitedForLinksNothing()
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            prompt.ShowBrowserStep(source, AccountsHarness.ApprovalPage, browserOpened: true);
            await Task.Delay(Timeout.Infinite, token);
            return AccountsHarness.Account(source, kind);
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        Task linking = nexus.LinkMethods[1].Command.ExecuteAsync(null);
        nexus.StopLinkingCommand.Execute(null);
        await linking.WaitAsync(Patience, Cancellation);

        Assert.False(nexus.IsLinked);
        Assert.False(nexus.IsWaitingForBrowser);
        Assert.False(nexus.IsBusy);
        Assert.Null(nexus.Note);
        Assert.Empty(_harness.Logger.Entries);
    }

    [Fact]
    public async Task WhenTheBrowserDoesntOpenThePageIsShownToOpenByHand()
    {
        await _harness.StartAsync();
        TaskCompletionSource approved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            prompt.ShowBrowserStep(source, AccountsHarness.ApprovalPage, browserOpened: false);
            await approved.Task.WaitAsync(token);
            return AccountsHarness.Account(source, kind);
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        Task linking = nexus.LinkMethods[1].Command.ExecuteAsync(null);
        string? note = nexus.BrowserNote;
        string? page = nexus.BrowserPageText;
        _harness.Browser.CanOpen = false;
        await nexus.OpenBrowserPageAgainCommand.ExecuteAsync(null);
        string? stillShown = nexus.BrowserPageText;
        _harness.Browser.CanOpen = true;
        await nexus.OpenBrowserPageAgainCommand.ExecuteAsync(null);
        (string? note, string? page) opened = (nexus.BrowserNote, nexus.BrowserPageText);
        approved.SetResult();
        await linking.WaitAsync(Patience, Cancellation);

        Assert.Equal("Your browser didn't open. Open this page, sign in to Nexus Mods and approve ModShelf:", note);
        Assert.Equal(AccountsHarness.ApprovalPage.AbsoluteUri, page);
        Assert.Equal(page, stillShown);
        Assert.Equal(
            ("Sign in to Nexus Mods in the page that opened in your browser, and approve ModShelf. Linking finishes here once you have.", null),
            opened);
        Assert.Equal(2, _harness.Browser.Opened.Count);
        Assert.Null(nexus.BrowserPageText);
        Assert.True(nexus.IsLinked);
    }

    [Fact]
    public async Task TheBrowserStepIsShownOnTheUiThread()
    {
        await _harness.StartAsync();
        TaskCompletionSource approved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Accounts.Link = async (source, kind, prompt, token) =>
        {
            prompt.ShowBrowserStep(source, AccountsHarness.ApprovalPage, browserOpened: true);
            await approved.Task.WaitAsync(token);
            return AccountsHarness.Account(source, kind);
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        _harness.UiThread.Inline = false;

        Task linking = nexus.LinkMethods[1].Command.ExecuteAsync(null);
        bool waitingBefore = nexus.IsWaitingForBrowser;
        _harness.UiThread.RunPending();
        bool waitingAfter = nexus.IsWaitingForBrowser;
        approved.SetResult();
        await linking.WaitAsync(Patience, Cancellation);

        Assert.False(waitingBefore);
        Assert.True(waitingAfter);
    }

    [Fact]
    public async Task ABrowserStepShownAfterLinkingEndedIsntShown()
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = (source, kind, prompt, _) =>
        {
            prompt.ShowBrowserStep(source, AccountsHarness.ApprovalPage, browserOpened: true);
            return Task.FromResult(AccountsHarness.Account(source, kind));
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        _harness.UiThread.Inline = false;

        await nexus.LinkMethods[1].Command.ExecuteAsync(null);
        _harness.UiThread.RunPending();

        Assert.False(nexus.IsWaitingForBrowser);
        Assert.Null(nexus.BrowserNote);
        Assert.True(nexus.IsLinked);
    }

    [Fact]
    public async Task CheckShowsTheAccountAndItsQuotaAsTheSiteSaysNow()
    {
        using CultureScope culture = new("en-US");
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        _harness.Time.Advance(TimeSpan.FromMinutes(5));
        _harness.Accounts.Check = (source, _) =>
        {
            _harness.Accounts.Quotas[source] = new ApiQuota([new QuotaAllowance(TimeSpan.FromHours(1), 2000, 1500, TestData.Now.AddHours(1))]);
            return Task.FromResult(AccountsHarness.Account(source, tier: "Supporter"));
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);
        bool quotaBefore = nexus.HasQuota;

        await nexus.CheckCommand.ExecuteAsync(null);

        Assert.False(quotaBefore);
        Assert.Equal("Supporter", nexus.Tier);
        Assert.Equal([$"Per hour: 1,500 of 2,000 left, until 10/27/2026 1:00{AmPm}PM"], nexus.Quota);
        Assert.Equal(($"Checked at 12:05{AmPm}PM.", false), (nexus.Note, nexus.IsNoteAProblem));
        Assert.False(nexus.IsBusy);
        Assert.Equal(["list", "check nexus", "list"], _harness.Accounts.Calls);
    }

    [Fact]
    public async Task CheckSaysWhenTheAccountWasUnlinkedElsewhere()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        _harness.Accounts.Check = (source, _) =>
        {
            _harness.Accounts.SetAccount(source, null);
            throw new KeyNotFoundException("No account is linked on nexus.");
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.CheckCommand.ExecuteAsync(null);

        Assert.False(nexus.IsLinked);
        Assert.Equal(("No account is linked on Nexus Mods any more.", true), (nexus.Note, nexus.IsNoteAProblem));
        Assert.False(nexus.CheckCommand.CanExecute(null));
    }

    [Theory]
    [MemberData(nameof(CheckProblems))]
    public async Task ACheckThatFailsKeepsTheAccountAndSaysWhatToDo(string problem, string note, bool logged)
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        _harness.Accounts.Check = (_, _) => throw Failure(problem);
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.CheckCommand.ExecuteAsync(null);

        Assert.True(nexus.IsLinked);
        Assert.Equal("Premium", nexus.Tier);
        Assert.Equal((note, true), (nexus.Note, nexus.IsNoteAProblem));
        Assert.False(nexus.IsBusy);
        Assert.True(nexus.CheckCommand.CanExecute(null));
        Assert.Equal(logged, _harness.Logger.Entries.Any(entry => entry.StartsWith("Error: Checking the account on nexus failed", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UnlinkingAsksFirst()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.UnlinkCommand.ExecuteAsync(null);

        Assert.Equal(
            ("Unlink Nexus Mods?", "ModShelf forgets Jean, your account on Nexus Mods, and deletes its stored credentials. Whatever needs the account stops working on Nexus Mods until one is linked again.", "Unlink"),
            Assert.Single(_harness.Dialogs.Confirmations));
        Assert.True(nexus.IsLinked);
        Assert.DoesNotContain("unlink nexus", _harness.Accounts.Calls);
    }

    [Fact]
    public async Task UnlinkingRemovesTheAccountAndItsQuota()
    {
        _harness.Accounts.Quotas[TestData.Nexus] = new ApiQuota([new QuotaAllowance(TimeSpan.FromDays(1), 10, 5, TestData.Now)]);
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        _harness.Dialogs.Confirm.Enqueue(true);
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.UnlinkCommand.ExecuteAsync(null);

        Assert.False(nexus.IsLinked);
        Assert.Equal((string.Empty, null, "No account is linked."), (nexus.AccountName, nexus.Tier, nexus.LinkedText));
        Assert.Equal([true, false], nexus.LinkMethods.Select(method => method.IsMain));
        Assert.False(nexus.HasQuota);
        Assert.Equal(("Unlinked. Its stored credentials are deleted.", false), (nexus.Note, nexus.IsNoteAProblem));
        Assert.False(nexus.UnlinkCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnUnlinkTheKeyringRefusesKeepsTheAccount()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Accounts.UnlinkRefusal = new InvalidOperationException("The Secret Service couldn't do that: the keyring is locked.");
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.UnlinkCommand.ExecuteAsync(null);

        Assert.True(nexus.IsLinked);
        Assert.Equal(
            ("The Secret Service couldn't do that: the keyring is locked. The account stays linked; unlink it again once that's fixed.", true),
            (nexus.Note, nexus.IsNoteAProblem));
        Assert.False(nexus.IsBusy);
    }

    [Fact]
    public async Task AnUnlinkThatFailsUnexpectedlyIsLogged()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        _harness.Dialogs.Confirm.Enqueue(true);
        _harness.Accounts.UnlinkRefusal = new NotSupportedException("Boom.");
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.UnlinkCommand.ExecuteAsync(null);

        Assert.Equal((Strings.SomethingWentWrong, true), (nexus.Note, nexus.IsNoteAProblem));
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Error: Unlinking the account on nexus failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALinkedAccountShowsWhenTheAccountsCantBeListedAgain()
    {
        await _harness.StartAsync();
        _harness.Accounts.Link = (source, kind, _, _) =>
        {
            _harness.Accounts.ListRefusal = new InvalidOperationException("The database is locked.");
            return Task.FromResult(AccountsHarness.Account(source, kind, name: "Robin"));
        };
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        await nexus.LinkMethods[0].Command.ExecuteAsync(null);

        Assert.True(nexus.IsLinked);
        Assert.Equal(("Robin", "Linked as Robin."), (nexus.AccountName, nexus.Note));
        Assert.Contains(_harness.Logger.Entries, entry => entry.StartsWith("Warning: The account on nexus couldn't be listed again after a change", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhileBusyTheOtherActionsWait()
    {
        await _harness.StartAsync(nexus: AccountsHarness.Account(TestData.Nexus));
        TaskCompletionSource<LinkedAccount> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _harness.Accounts.Check = (_, _) => answer.Task;
        AccountViewModel nexus = _harness.Card(TestData.Nexus);

        // The buttons ask again only when told to.
        int[] linkButtonsTold = new int[nexus.LinkMethods.Count];
        for (int index = 0; index < linkButtonsTold.Length; index++)
        {
            int method = index;
            nexus.LinkMethods[method].Command.CanExecuteChanged += (_, _) => linkButtonsTold[method]++;
        }

        Task checking = nexus.CheckCommand.ExecuteAsync(null);
        bool[] whileBusy = [nexus.CheckCommand.CanExecute(null), nexus.UnlinkCommand.CanExecute(null), .. nexus.LinkMethods.Select(method => method.Command.CanExecute(null))];
        int[] toldWhileBusy = [.. linkButtonsTold];
        answer.SetResult(AccountsHarness.Account(TestData.Nexus));
        await checking.WaitAsync(Patience, Cancellation);

        Assert.True(whileBusy.All(canExecute => !canExecute));
        Assert.True(nexus.CheckCommand.CanExecute(null));
        Assert.All(toldWhileBusy, told => Assert.Equal(1, told));
        Assert.All(linkButtonsTold, told => Assert.Equal(2, told));
    }

    private static Exception Failure(string problem) => problem switch
    {
        "refused" => new AuthenticationFailedException("Refused."),
        "slow down" => new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests),
        "error" => new HttpRequestException("Bad gateway.", null, HttpStatusCode.BadGateway),
        "unreachable" => new HttpRequestException("No such host is known."),
        "timeout" => new TaskCanceledException("The request was canceled.", new TimeoutException()),
        "blank" => new ArgumentException("No API key was entered.", nameof(problem)),
        "keyring" => new InvalidOperationException("The Secret Service couldn't do that: the keyring is locked."),
        _ => new NotSupportedException("Boom."),
    };
}
