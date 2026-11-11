using ModelContextProtocol.Client;

using ModShelf.Cli.Mcp;
using ModShelf.Cli.Tests.Common;
using ModShelf.Cli.Tests.Fakes;

namespace ModShelf.Cli.Tests.Mcp;

/// <summary>
/// Whether agents may use ModShelf (<c>agents.enabled</c>): off until the user turns it on, the server still starts and
/// lists its tools, and refuses every call, saying so; the setting is read at each call; and it's the user's alone.
/// </summary>
public sealed class AgentAccessTests : IAsyncDisposable
{
    private readonly FakeAccountSite _site = new();
    private readonly ScriptedBrowser _browser = new() { UserActs = false };
    private InProcessCli? _cli;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private InProcessCli Cli => _cli ??= new InProcessCli(_site, _browser);

    public async ValueTask DisposeAsync()
    {
        if (_cli is not null)
        {
            await _cli.DisposeAsync();
        }

        _browser.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task OffByDefaultItListsItsToolsAndRefusesEveryCall()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(agentsAllowed: false, Cancellation);

        IList<McpClientTool> tools = await mcp.Client.ListToolsAsync(cancellationToken: Cancellation);
        string settings = await mcp.CallFailingAsync("settings_show", null, Cancellation);
        string diagnostics = await mcp.CallFailingAsync("diagnostics", null, Cancellation);

        Assert.Equal(26, tools.Count);
        Assert.EndsWith(AgentAccess.Off, settings, StringComparison.Ordinal);
        Assert.EndsWith(AgentAccess.Off, diagnostics, StringComparison.Ordinal);
        Assert.Contains("Ask the user to turn on \"Allow agents to use ModShelf\" in ModShelf's Settings, Agents", AgentAccess.Off, StringComparison.Ordinal);
        Assert.Contains(Cli.Logs.Entries, entry => entry.Contains("Refused a call of settings_show: agents are off", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TurnedOnMeanwhileTheNextCallGoesOn()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(agentsAllowed: false, Cancellation);
        await mcp.CallFailingAsync("settings_show", null, Cancellation);

        CliResult allowed = await Cli.RunAsync(["settings", "set", "agents.enabled", "true"], Cancellation);
        SettingsListing listing = await mcp.CallAsync<SettingsListing>("settings_show", null, Cancellation);

        Assert.Equal(0, allowed.ExitCode);
        Assert.Equal("true", Assert.Single(listing.Settings, setting => setting.Name == "agents.enabled").Value);

        // And off again.
        await Cli.RunAsync(["settings", "reset", "agents.enabled"], Cancellation);
        Assert.EndsWith(AgentAccess.Off, await mcp.CallFailingAsync("settings_show", null, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsThatCantBeReadCountAsOff()
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Cli.Folder, "settings.json"), "{ not JSON", Cancellation);

        string refused = await mcp.CallFailingAsync("library_list", null, Cancellation);

        Assert.Contains("ModShelf's settings can't be read, so AI agents count as off: The settings in ", refused, StringComparison.Ordinal);
        Assert.EndsWith("Ask the user to fix them.", refused, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("settings_set", "agents.enabled", "false")]
    [InlineData("settings_set", "agents.port", "50000")]
    [InlineData("settings_reset", "agents.enabled", null)]
    [InlineData("settings_reset", " AGENTS.PORT ", null)]
    public async Task AnAgentCantChangeItsOwnAccess(string tool, string setting, string? value)
    {
        await using McpSession mcp = await Cli.StartMcpAsync(Cancellation);

        string refused = await mcp.CallFailingAsync(
            tool,
            value is null ? new Dictionary<string, object?> { ["setting"] = setting } : new Dictionary<string, object?> { ["setting"] = setting, ["value"] = value },
            Cancellation);

        Assert.EndsWith(SettingsTools.AgentAccessRefused, refused, StringComparison.Ordinal);
        string file = await File.ReadAllTextAsync(Path.Combine(Cli.Folder, "settings.json"), Cancellation);
        Assert.Contains("\"enabled\": true", file, StringComparison.Ordinal);
        Assert.DoesNotContain("port", file, StringComparison.Ordinal);
    }
}
