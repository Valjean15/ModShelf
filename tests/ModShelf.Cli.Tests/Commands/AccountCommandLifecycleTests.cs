using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// Phase 2's promise, end to end on each operating system: a fake OAuth source and a fake API-key source can each be
/// linked, survive an app restart, refresh an expired token, and unlink cleanly. The app is the CLI's own host, with
/// the system's real secret store, the real database and the real OAuth loopback redirect; only the site is fake.
/// </summary>
public sealed class AccountCommandLifecycleTests : IAsyncDisposable
{
    private const int Done = 0;
    private const string Me = "/api/me";

    private static readonly string OAuthSource = FakeOAuthSource.SourceId.Value;
    private static readonly string KeySource = FakeApiKeySource.SourceId.Value;

    private readonly ITestOutputHelper _log;
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly InProcessCli _cli;
    private readonly List<CliResult> _runs = [];

    public AccountCommandLifecycleTests(ITestOutputHelper log)
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
    public async Task AnOAuthAccountSurvivesRestartsRefreshesAndUnlinksCleanly()
    {
        Assert.Equal(Done, (await RunAsync("account", "link", OAuthSource)).ExitCode);
        string linkedWith = Assert.Single(_site.RequestsTo(Me)).Credential!;

        // After a restart, the source's requests carry the account it was linked with.
        Assert.Equal([(linkedWith, HttpStatusCode.OK)], await SendAfterRestartAsync<FakeOAuthSource>());
        Assert.Equal(0, _site.TokenRefreshes);

        // The site expires the token early: the next request is refused, refreshed once, and sent again.
        _site.ExpireAccessTokens();
        string refreshed = await AssertRefreshedOnceAsync(linkedWith);

        // The refreshed tokens were stored: after another restart they're used as they are.
        Assert.Equal([(refreshed, HttpStatusCode.OK)], await SendAfterRestartAsync<FakeOAuthSource>());
        Assert.Equal(1, _site.TokenRefreshes);
        Assert.Equal(FakeAccountSite.OAuthUser, Cells(await ListedAsync(OAuthSource))[1]);

        await AssertUnlinksCleanlyAsync<FakeOAuthSource>(OAuthSource, "-");
    }

    [Fact]
    public async Task AnApiKeyAccountSurvivesRestartsAndUnlinksCleanly()
    {
        Assert.Equal(Done, (await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n")).ExitCode);

        Assert.Equal([(_site.ApiKey, HttpStatusCode.OK)], await SendAfterRestartAsync<FakeApiKeySource>());
        Assert.Equal([(_site.ApiKey, HttpStatusCode.OK)], await SendAfterRestartAsync<FakeApiKeySource>());
        Assert.Equal(FakeAccountSite.KeyUser, Cells(await ListedAsync(KeySource))[1]);

        await AssertUnlinksCleanlyAsync<FakeApiKeySource>(KeySource, "- (optional)");
    }

    [Fact]
    public async Task TokensAboutToExpireAreRefreshedBeforeUse()
    {
        // Shorter than the minute ahead of expiry in which the app refreshes.
        _site.ExpiresIn = TimeSpan.FromSeconds(30);
        Assert.Equal(Done, (await RunAsync("account", "link", OAuthSource)).ExitCode);
        string linkedWith = Assert.Single(_site.RequestsTo(Me)).Credential!;

        (string Credential, HttpStatusCode Status) sent = Assert.Single(await SendAfterRestartAsync<FakeOAuthSource>());

        // No request went out with the old token, so none was refused.
        Assert.Equal(1, _site.TokenRefreshes);
        Assert.NotEqual(linkedWith, sent.Credential);
        Assert.Equal(HttpStatusCode.OK, sent.Status);
    }

    [Fact]
    public async Task NoCredentialIsShownLoggedOrStoredInPlainText()
    {
        Assert.Equal(Done, (await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n")).ExitCode);
        Assert.Equal(Done, (await RunAsync("account", "link", OAuthSource)).ExitCode);
        string bearer = Assert.Single(_site.RequestsTo(Me), request => request.Credential!.StartsWith("Bearer ", StringComparison.Ordinal)).Credential!;
        _site.ExpireAccessTokens();
        await AssertRefreshedOnceAsync(bearer);
        await RunAsync("account", "list");

        // The API key; an authorization code and its verifier; the tokens from linking and from the refresh.
        IReadOnlyList<string> secrets = _site.Secrets;
        Assert.Equal(7, secrets.Count);

        string shown = string.Concat(_runs.Select(run => run.Output + run.Errors));
        string logged = string.Join(Environment.NewLine, _cli.Logs.Entries);
        Assert.NotEmpty(_cli.Logs.Entries);
        string[] files = Directory.GetFiles(_cli.Folder, "*", SearchOption.AllDirectories);
        Assert.Contains(files, file => Path.GetFileName(file) == "library.db");
        foreach (string secret in secrets)
        {
            Assert.DoesNotContain(secret, shown, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logged, StringComparison.Ordinal);
            foreach (string file in files)
            {
                byte[] content = await ReadSharedAsync(file);
                Assert.True(
                    content.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) < 0,
                    $"{Path.GetRelativePath(_cli.Folder, file)} holds a credential in plain text.");
            }
        }
    }

    private static string[] Cells(string row) => row.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Reads a file another handle may still have open for writing, such as the database in SQLite's connection pool,
    /// which Windows otherwise refuses.
    /// </summary>
    private static async Task<byte[]> ReadSharedAsync(string path)
    {
        await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream content = new();
        await file.CopyToAsync(content, Cancellation);
        return content.ToArray();
    }

    private static Task<AccountProfile> WhoAmIAsync<TSource>(IServiceProvider services)
        where TSource : IFakeAccountSource
        => services.GetServices<IModSource>().OfType<TSource>().Single().WhoAmIAsync(Cancellation);

    /// <summary>
    /// Restarts the app, and has the source ask the site whose account its requests carry.
    /// </summary>
    /// <returns>The credential and the site's answer for each request the source's call made.</returns>
    private async Task<(string Credential, HttpStatusCode Status)[]> SendAfterRestartAsync<TSource>()
        where TSource : IFakeAccountSource
    {
        int before = _site.RequestsTo(Me).Count;
        AccountProfile profile = await _cli.InAppAsync(WhoAmIAsync<TSource>, Cancellation);
        _log.WriteLine($"{typeof(TSource).Name} is linked to {profile.DisplayName}.");
        return [.. _site.RequestsTo(Me).Skip(before).Select(request => (request.Credential!, request.Status))];
    }

    /// <returns>The credential sent after the refresh.</returns>
    private async Task<string> AssertRefreshedOnceAsync(string expired)
    {
        int refreshes = _site.TokenRefreshes;

        (string Credential, HttpStatusCode Status)[] sent = await SendAfterRestartAsync<FakeOAuthSource>();

        Assert.Equal(refreshes + 1, _site.TokenRefreshes);
        Assert.Equal(2, sent.Length);
        Assert.Equal((expired, HttpStatusCode.Unauthorized), sent[0]);
        Assert.NotEqual(expired, sent[1].Credential);
        Assert.Equal(HttpStatusCode.OK, sent[1].Status);
        return sent[1].Credential;
    }

    /// <summary>
    /// Unlinks the source, then checks that nothing of the account is left: not in the list, not in the secret store,
    /// and not on the source's requests.
    /// </summary>
    private async Task AssertUnlinksCleanlyAsync<TSource>(string source, string listedWithoutAccount)
        where TSource : IFakeAccountSource
    {
        CliResult unlinked = await RunAsync("account", "unlink", source);
        Assert.Equal(Done, unlinked.ExitCode);
        Assert.Contains($"Unlinked {source}. Its stored credentials are deleted.", unlinked.Output, StringComparison.Ordinal);

        Assert.Equal(listedWithoutAccount, Cells(await ListedAsync(source))[1]);

        // The account service keeps a source's credentials under account/<source>, as ISecretStore's names go.
        string? stored = await _cli.InAppAsync(
            services => services.GetRequiredService<ISecretStore>().ReadAsync($"account/{source}", Cancellation),
            Cancellation);
        Assert.Null(stored);

        int before = _site.RequestsTo(Me).Count;
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => _cli.InAppAsync(WhoAmIAsync<TSource>, Cancellation));
        Assert.Null(Assert.Single(_site.RequestsTo(Me).Skip(before)).Credential);
    }

    private async Task<string> ListedAsync(string source) => (await RunAsync("account", "list")).AccountRowFor(source);

    private Task<CliResult> RunAsync(params string[] arguments) => RunAsync(arguments, string.Empty);

    private async Task<CliResult> RunAsync(string[] arguments, string input)
    {
        CliResult result = await _cli.RunAsync(arguments, Cancellation, input);
        _log.WriteLine(result.ToString());
        _runs.Add(result);
        return result;
    }
}
