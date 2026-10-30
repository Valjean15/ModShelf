using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModShelf.Application.Links;
using ModShelf.Application.Links.Ports;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;
using ModShelf.Testing;

namespace ModShelf.Cli.Tests.Commands;

/// <summary>
/// <c>links</c>: which app opens Nexus Mods' <c>nxm://</c> links, in a register of the test's own, so the system's is
/// never touched.
/// </summary>
public sealed class LinksCommandTests : IAsyncDisposable
{
    private const int Done = 0;
    private const int Failed = 1;
    private const int BadInput = 2;

    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new();
    private readonly MemorySchemeRegistration _registration = new();
    private readonly InProcessCli _cli;

    public LinksCommandTests()
    {
        _cli = new InProcessCli(_site, _browser, configureServices: services =>
        {
            services.RemoveAll<ISchemeRegistration>();
            services.AddSingleton<ISchemeRegistration>(_registration);
        });
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _cli.DisposeAsync();
        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task StatusSaysWhichAppOpensEachSitesLinks()
    {
        _registration.States["nxm"] = new SchemeState(SchemeHandlerKind.OtherApp, "Vortex");

        CliResult status = await _cli.RunAsync(["links", "status"], Cancellation);

        Assert.Equal(Done, status.ExitCode);
        string[] lines = Lines(status.Output);
        Assert.Matches(@"^Site\s+Links\s+Opened by$", lines[0]);
        Assert.Matches(@"^nexus\s+nxm://\s+Vortex$", Assert.Single(lines, line => line.StartsWith("nexus", StringComparison.Ordinal)));
        Assert.Equal("To make the desktop app open a site's links: links take <site>", lines[^1]);
    }

    [Fact]
    public async Task StatusExplainsWhyItsUnavailable()
    {
        _registration.States["nxm"] = SchemeState.Unavailable;

        CliResult status = await _cli.RunAsync(["links", "status"], Cancellation);

        Assert.Matches(@"nexus\s+nxm://\s+unavailable", status.Output);
        Assert.Contains("ModShelf__DesktopApp", status.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TakeMakesTheDesktopAppOpenThem()
    {
        _registration.States["nxm"] = new SchemeState(SchemeHandlerKind.OtherApp, "Vortex");

        CliResult taken = await _cli.RunAsync(["links", "take", "nexus"], Cancellation);

        Assert.Equal(Done, taken.ExitCode);
        Assert.Equal(["Register nxm"], _registration.Calls);
        Assert.Equal(
            "The desktop app opens nxm:// links from Nexus Mods now. Vortex opened them before; links give-back nexus gives them back to it.",
            taken.Output.Trim());
        Assert.Matches(@"nexus\s+nxm://\s+ModShelf \(before it, Vortex\)", (await _cli.RunAsync(["links", "status"], Cancellation)).Output);
    }

    [Fact]
    public async Task TheOnlySiteCanBeLeftOut()
    {
        CliResult taken = await _cli.RunAsync(["links", "take"], Cancellation);
        CliResult givenBack = await _cli.RunAsync(["links", "give-back"], Cancellation);

        Assert.Equal((Done, Done), (taken.ExitCode, givenBack.ExitCode));
        Assert.Equal("The desktop app opens nxm:// links from Nexus Mods now. links give-back nexus stops it.", taken.Output.Trim());
        Assert.Equal("ModShelf no longer opens nxm:// links from Nexus Mods. No app opens them now.", givenBack.Output.Trim());
        Assert.Equal(["Register nxm", "Unregister nxm"], _registration.Calls);
    }

    [Fact]
    public async Task GiveBackSaysWhoOpensThemNow()
    {
        _registration.States["nxm"] = new SchemeState(SchemeHandlerKind.ModShelf, PreviousApp: "Vortex");

        CliResult givenBack = await _cli.RunAsync(["links", "give-back", "nexus"], Cancellation);

        Assert.Equal(Done, givenBack.ExitCode);
        Assert.Equal("ModShelf no longer opens nxm:// links from Nexus Mods. Vortex opens them now.", givenBack.Output.Trim());
    }

    [Fact]
    public async Task AnUnknownSiteIsBadInput()
    {
        CliResult taken = await _cli.RunAsync(["links", "take", "moddb"], Cancellation);

        Assert.Equal(BadInput, taken.ExitCode);
        Assert.Equal("No site 'moddb' hands its downloads over with links: links status lists them.", taken.Errors.Trim());
        Assert.Empty(_registration.Calls);
    }

    [Fact]
    public async Task ARegisterThatCantTakeThemFails()
    {
        _registration.States["nxm"] = SchemeState.Unavailable;

        CliResult taken = await _cli.RunAsync(["links", "take", "nexus"], Cancellation);

        Assert.Equal(Failed, taken.ExitCode);
        Assert.Equal("ModShelf can't be registered here.", taken.Errors.Trim());
    }

    [Fact]
    public async Task ARegisterThatCantBeChangedSaysWhy()
    {
        _registration.Failure = new UnauthorizedAccessException("Access to the registry key is denied.");

        CliResult taken = await _cli.RunAsync(["links", "take", "nexus"], Cancellation);

        Assert.Equal(Failed, taken.ExitCode);
        Assert.Equal("The system's apps for links couldn't be changed: Access to the registry key is denied.", taken.Errors.Trim());
    }

    private static string[] Lines(string output) => output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
