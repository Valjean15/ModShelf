using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Application.Accounts.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>account link</c> and <c>unlink</c> with a keyring that's there but fails, such as one that's locked.
/// </summary>
public sealed class AccountCommandKeyringTests : IAsyncDisposable
{
    private const int Done = 0;
    private const int Failed = 1;

    private static readonly string KeySource = FakeApiKeySource.SourceId.Value;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly FailingSecretStore _keyring = new();
    private readonly InProcessCli _cli;

    public AccountCommandKeyringTests()
    {
        _cli = new InProcessCli(_site, _browser, configureServices: services =>
        {
            services.RemoveAll<ISecretStore>();
            services.AddSingleton<ISecretStore>(_keyring);
        });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        _keyring.Failing = false;
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task ALinkTheKeyringCantKeepLinksNothingAndSaysWhy()
    {
        CliResult linked = await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n");

        Assert.Equal(Failed, linked.ExitCode);
        Assert.EndsWith($"{FailingSecretStore.Problem} Nothing was linked.", linked.Errors.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain(FakeAccountSite.KeyUser, (await RunAsync(["account", "list"])).AccountRowFor(KeySource), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnlinkTheKeyringRefusesKeepsTheAccountAndSaysWhy()
    {
        _keyring.Failing = false;
        Assert.Equal(Done, (await RunAsync(["account", "link", KeySource], _site.ApiKey + "\n")).ExitCode);
        _keyring.Failing = true;

        CliResult unlinked = await RunAsync(["account", "unlink", KeySource]);

        Assert.Equal(Failed, unlinked.ExitCode);
        Assert.EndsWith(
            $"{FailingSecretStore.Problem} The account stays linked; unlink it again once that's fixed.",
            unlinked.Errors.Trim(),
            StringComparison.Ordinal);
        Assert.Contains(FakeAccountSite.KeyUser, (await RunAsync(["account", "list"])).AccountRowFor(KeySource), StringComparison.Ordinal);
    }

    private Task<CliResult> RunAsync(string[] arguments, string input = "") => _cli.RunAsync(arguments, Cancellation, input);
}
