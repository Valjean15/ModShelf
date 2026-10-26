using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Cli.Hosting;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Platform;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>account list</c>, <c>link</c> and <c>unlink</c> as the user runs them, in-process, against the fake site through
/// the real secret store, database and OAuth loopback redirect.
/// </summary>
public sealed partial class AccountCommandTests : IAsyncDisposable
{
    // Exit codes are the CLI's contract with scripts, so they're spelled out here rather than shared.
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;
    private const int Interrupted = 130;

    private const string KeyringWarning = "No keyring is running";

    private static readonly string OAuthSource = FakeOAuthSource.SourceId.Value;
    private static readonly string KeySource = FakeApiKeySource.SourceId.Value;

    private readonly ITestOutputHelper _log;
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;

    public AccountCommandTests(ITestOutputHelper log)
    {
        _log = log;
        _cli = new InProcessCli(_site, _browser);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ListShowsEachSourceThatCanLinkAnAccount()
    {
        CliResult listed = await RunAsync(["account", "list"]);

        Assert.Equal(Done, listed.ExitCode);
        Assert.Equal(["Source", "Account", "Level", "Linked on", "Link with"], Cells(listed.Output.ReplaceLineEndings("\n").Split('\n')[0]));
        Assert.Equal([KeySource, "- (optional)", "-", "-", "api-key"], Cells(listed.AccountRowFor(KeySource)));
        Assert.Equal([OAuthSource, "-", "-", "-", "browser"], Cells(listed.AccountRowFor(OAuthSource)));
        Assert.Equal(["nexus", "-", "-", "-", "api-key"], Cells(listed.AccountRowFor("nexus")));
        Assert.DoesNotContain("direct", listed.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "api-key")]
    [InlineData(" ", "api-key")]
    [InlineData("client-from-nexus", "browser, api-key")]
    public async Task NexusOffersTheBrowserOnceItsClientIdIsSet(string? clientId, string linkWith)
    {
        await using InProcessCli cli = new(_site, _browser, new Dictionary<string, string?> { [CliHost.NexusClientIdKey] = clientId });

        CliResult listed = Log(await cli.RunAsync(["account", "list"], Cancellation));

        Assert.Equal(["nexus", "-", "-", "-", linkWith], Cells(listed.AccountRowFor("nexus")));
    }

    [Fact]
    public async Task AnApiKeyLinksTheAccountItBelongsTo()
    {
        string today = Today();
        CliResult linked = await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n");

        Assert.Equal(Done, linked.ExitCode);
        Assert.Contains($"Find your API key for {KeySource} at {_site.KeyPage}", linked.Output, StringComparison.Ordinal);
        Assert.Contains("API key (not shown as you type): ", linked.Output, StringComparison.Ordinal);
        Assert.Contains($"Linked {KeySource} as {FakeAccountSite.KeyUser} ({FakeAccountSite.KeyTier}).", linked.Output, StringComparison.Ordinal);
        Assert.Empty(linked.Errors);
        Assert.DoesNotContain(_site.ApiKey, linked.Output, StringComparison.Ordinal);

        string[] row = Cells((await RunAsync(["account", "list"])).AccountRowFor(KeySource));
        Assert.Equal([KeySource, FakeAccountSite.KeyUser, FakeAccountSite.KeyTier], row[..3]);
        string[] linkedOn = [today, Today()];
        Assert.Contains(row[3], linkedOn);
    }

    [Fact]
    public async Task AKeyTheSiteRefusesLinksNothing()
    {
        CliResult refused = await RunAsync(["account", "link", KeySource, "--with", "api-key"], "key-00000000\n");

        Assert.Equal(Failed, refused.ExitCode);
        Assert.Contains($"{KeySource} refused the API key. Check it, then try again. Nothing was linked.", refused.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain("Linked", refused.Output, StringComparison.Ordinal);
        Assert.Equal("- (optional)", Cells((await RunAsync(["account", "list"])).AccountRowFor(KeySource))[1]);
    }

    [Fact]
    public async Task ABlankKeyIsRefusedWithoutAskingTheSite()
    {
        CliResult refused = await RunAsync(["account", "link", KeySource], "   \n");

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains("No API key was entered. Nothing was linked.", refused.Errors, StringComparison.Ordinal);
        Assert.Empty(_site.RequestsTo("/api/me"));
    }

    [Fact]
    public async Task ApprovingInTheBrowserLinksTheAccount()
    {
        CliResult linked = await RunAsync(["account", "link", OAuthSource]);

        Assert.Equal(Done, linked.ExitCode);
        Uri page = Assert.Single(_browser.Opened);
        Assert.Equal("/oauth/authorize", page.AbsolutePath);
        Assert.Contains(
            $"Sign in to {OAuthSource} in the page that opened in your browser, and approve ModShelf. If no page opened, open this one:",
            linked.Output,
            StringComparison.Ordinal);
        Assert.Contains(page.AbsoluteUri, linked.Output, StringComparison.Ordinal);
        Assert.Contains($"Linked {OAuthSource} as {FakeAccountSite.OAuthUser} ({FakeAccountSite.OAuthTier}).", linked.Output, StringComparison.Ordinal);

        // The site sent the browser back to the app, which told the user to return to ModShelf.
        BrowserVisit visit = await Assert.Single(_browser.Visits);
        Assert.Equal(HttpStatusCode.OK, visit.Status);
        Assert.Equal("127.0.0.1", visit.EndedAt.Host);
        Assert.NotEqual(_site.BaseAddress.Port, visit.EndedAt.Port);
        Assert.Contains("ModShelf", visit.Content, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, Assert.Single(_site.RequestsTo("/oauth/token")).Status);
    }

    [Fact]
    public async Task WithoutABrowserThePageIsShownToOpenByHand()
    {
        _browser.Opens = false;
        await using CliRun run = _cli.Start(["account", "link", OAuthSource], TextReader.Null);

        await run.WaitForOutputAsync("Waiting for your approval. Press Ctrl+C to stop.", Cancellation);
        Assert.Contains($"Open this page in your browser, sign in to {OAuthSource} and approve ModShelf:", run.Output, StringComparison.Ordinal);
        string shown = Assert.Single(Lines(run.Output), line => line.StartsWith(_site.BaseAddress.AbsoluteUri, StringComparison.Ordinal));
        await _browser.VisitAsync(new Uri(shown));
        CliResult linked = Log(await run.WaitForExitAsync(Cancellation));

        Assert.Equal(Done, linked.ExitCode);
        Assert.Contains($"Linked {OAuthSource} as {FakeAccountSite.OAuthUser}", linked.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecliningInTheBrowserLinksNothing()
    {
        _site.UserDeclines = true;

        CliResult declined = await RunAsync(["account", "link", OAuthSource, "--with", "browser"]);

        Assert.Equal(Failed, declined.ExitCode);
        Assert.Contains(
            $"{OAuthSource} didn't let ModShelf in. Try again, and approve ModShelf in the browser. Nothing was linked.",
            declined.Errors,
            StringComparison.Ordinal);
        Assert.Empty(_site.RequestsTo("/oauth/token"));
        Assert.Equal("-", Cells((await RunAsync(["account", "list"])).AccountRowFor(OAuthSource))[1]);
    }

    [Fact]
    public async Task CtrlCWhileWaitingForTheBrowserLinksNothing()
    {
        _browser.UserActs = false;
        CliResult stopped;
        await using (CliRun run = _cli.Start(["account", "link", OAuthSource], TextReader.Null))
        {
            await run.WaitForOutputAsync("Waiting for your approval. Press Ctrl+C to stop.", Cancellation);
            await run.PressCtrlCAsync();
            stopped = Log(await run.WaitForExitAsync(Cancellation));
        }

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Contains("Stopped. Nothing was linked.", stopped.Errors, StringComparison.Ordinal);
        Assert.Equal("-", Cells((await RunAsync(["account", "list"])).AccountRowFor(OAuthSource))[1]);
    }

    [Fact]
    public async Task CtrlCWhileAskingForTheKeyLinksNothing()
    {
        using ControlledTextReader keyboard = new();
        CliResult stopped;
        await using (CliRun run = _cli.Start(["account", "link", KeySource], keyboard))
        {
            await run.WaitForOutputAsync("API key (not shown as you type): ", Cancellation);
            await run.PressCtrlCAsync();
            stopped = Log(await run.WaitForExitAsync(Cancellation));
        }

        Assert.Equal(Interrupted, stopped.ExitCode);
        Assert.Contains("Stopped. Nothing was linked.", stopped.Errors, StringComparison.Ordinal);
        Assert.Empty(_site.RequestsTo("/api/me"));
    }

    [Fact]
    public async Task UnlinkRemovesTheAccount()
    {
        Assert.Equal(Done, (await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n")).ExitCode);

        CliResult unlinked = await RunAsync(["account", "unlink", KeySource]);
        CliResult again = await RunAsync(["account", "unlink", KeySource]);

        Assert.Equal(Done, unlinked.ExitCode);
        Assert.Contains($"Unlinked {KeySource}. Its stored credentials are deleted.", unlinked.Output, StringComparison.Ordinal);
        Assert.Equal(Done, again.ExitCode);
        Assert.Contains($"No account is linked on {KeySource}.", again.Output, StringComparison.Ordinal);
        Assert.Equal("- (optional)", Cells((await RunAsync(["account", "list"])).AccountRowFor(KeySource))[1]);
    }

    [Fact]
    public async Task UnlinkingASourceThatIsntHereStillCleansUp()
    {
        CliResult unlinked = await RunAsync(["account", "unlink", "no-such-site"]);

        Assert.Equal(Done, unlinked.ExitCode);
        Assert.Contains("no-such-site isn't a source here. Any account stored for it is removed.", unlinked.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("link", "no-such-site", "No source called no-such-site supports accounts. Run account list to see the ones that do.")]
    [InlineData("link", "Nexus!", "Nexus! isn't a source ID.")]
    [InlineData("unlink", "Nexus!", "Nexus! isn't a source ID.")]
    public async Task SourcesThatDontExistAreBadInput(string command, string source, string message)
    {
        CliResult refused = await RunAsync(["account", command, source]);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fake-key", "browser", "fake-key can't be linked with --with browser. It offers: api-key.")]
    [InlineData("fake-oauth", "api-key", "fake-oauth can't be linked with --with api-key. It offers: browser.")]
    [InlineData("fake-key", "password", "Use --with api-key, browser or email.")]
    public async Task WaysASourceDoesntOfferAreBadInput(string source, string method, string message)
    {
        CliResult refused = await RunAsync(["account", "link", source, "--with", method]);

        Assert.Equal(BadInput, refused.ExitCode);
        Assert.Contains(message, refused.Errors, StringComparison.Ordinal);
        Assert.Empty(_site.Requests);
    }

    [Fact]
    public async Task TheCommandsSayWhenNoKeyringProtectsTheCredentials()
    {
        bool protectedByOs = await _cli.InAppAsync(
            services => Task.FromResult(services.GetRequiredService<ISecretStore>().IsProtectedByOs),
            Cancellation);

        CliResult listed = await RunAsync(["account", "list"]);
        CliResult linked = await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n");

        Assert.Equal(!protectedByOs, listed.Output.Contains(KeyringWarning, StringComparison.Ordinal));
        Assert.Equal(!protectedByOs, linked.Output.Contains(KeyringWarning, StringComparison.Ordinal));

        // The commands' own words replace the secret store's log warning.
        Assert.DoesNotContain(_cli.Logs.Entries, entry => entry.StartsWith("[ModShelf.Platform.Secrets.SecretStores]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecretsGoToTheFolderAndScopeTheSettingsName()
    {
        Assert.Equal(Done, (await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n")).ExitCode);

        string? ownStore = await _cli.InAppAsync(
            services => services.GetRequiredService<ISecretStore>().ReadAsync($"account/{KeySource}", Cancellation),
            Cancellation);

        // The user's own ModShelf secrets, only read here: the tests' secrets never reach them.
        ServiceCollection services = new();
        services.AddLogging();
        services.AddPlatformServices();
        await using ServiceProvider defaults = services.BuildServiceProvider();
        string? userStore = await defaults.GetRequiredService<ISecretStore>().ReadAsync($"account/{KeySource}", Cancellation);

        Assert.NotNull(ownStore);
        Assert.Null(userStore);
    }

    private static string[] Lines(string output) => output.ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// A table row's cells. Cells are two or more spaces apart, so a single space stays inside a cell.
    /// </summary>
    private static string[] Cells(string row) => CellGap().Split(row.Trim());

    private static string Today() => TimeProvider.System.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [GeneratedRegex(" {2,}")]
    private static partial Regex CellGap();

    private async Task<CliResult> RunAsync(string[] arguments, string input = "")
        => Log(await _cli.RunAsync(arguments, Cancellation, input));

    private CliResult Log(CliResult result)
    {
        _log.WriteLine(result.ToString());
        return result;
    }
}
