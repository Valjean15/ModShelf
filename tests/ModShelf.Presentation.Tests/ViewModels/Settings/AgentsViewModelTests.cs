using ModShelf.Application.Agents.Ports;
using ModShelf.Application.Settings;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Settings;

namespace ModShelf.Presentation.Tests.ViewModels.Settings;

/// <summary>
/// Settings › Agents: whether AI agents may use ModShelf, off until the user turns it on; the address the app serves
/// meanwhile, and its port; and connecting an agent: the prompt, the details, and Claude Desktop.
/// </summary>
public sealed class AgentsViewModelTests
{
    private const string Installed = @"C:\Users\kestrel\AppData\Local\ModShelf.App\current\ModShelf.Cli.exe";

    private readonly SettingsHarness _harness = new();

    private AgentsViewModel Agents => _harness.Agents;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AgentsAreOffUntilTheUserTurnsThemOn()
    {
        await _harness.StartAsync();

        Assert.Same(Agents, _harness.Screen.Agents);
        Assert.Equal((false, "47823"), (Agents.IsEnabled, Agents.PortText));
        Assert.Equal([(false, AppSettings.DefaultAgentsPort)], _harness.AgentServer.Applied);
        Assert.Equal(("Not served: agents are off.", false), (Agents.Status, Agents.HasStatusProblem));
        Assert.Empty(_harness.Settings.Changes);
        Assert.Equal(Installed, Agents.CommandNote);
    }

    [Fact]
    public async Task AllowedAsStoredTheAddressIsServedAtItsPort()
    {
        _harness.Settings.Stored = new AppSettings { AgentsEnabled = true, AgentsPort = 50000 };

        await Agents.LoadAsync(Cancellation);

        Assert.Equal((true, "50000"), (Agents.IsEnabled, Agents.PortText));
        Assert.Equal([(true, 50000)], _harness.AgentServer.Applied);
        Assert.Equal(("Served at http://127.0.0.1:50000/mcp while ModShelf runs.", false), (Agents.Status, Agents.HasStatusProblem));
        Assert.Empty(_harness.Settings.Changes);
    }

    [Fact]
    public async Task TurningThemOnSavesItAndServesTheAddressAndOffSetsItBack()
    {
        await Agents.LoadAsync(Cancellation);

        Agents.IsEnabled = true;
        await Agents.Changing;

        Assert.True(_harness.Settings.Stored.AgentsEnabled);
        Assert.Equal((true, AppSettings.DefaultAgentsPort), _harness.AgentServer.Applied[^1]);
        Assert.Equal("Served at http://127.0.0.1:47823/mcp while ModShelf runs.", Agents.Status);

        Agents.IsEnabled = false;
        await Agents.Changing;

        Assert.Equal(["agentsEnabled True", "agentsEnabled null"], _harness.Settings.Changes);
        Assert.Null(_harness.Settings.Stored.AgentsEnabled);
        Assert.Equal((false, AppSettings.DefaultAgentsPort), _harness.AgentServer.Applied[^1]);
        Assert.Equal("Not served: agents are off.", Agents.Status);
        Assert.Null(Agents.Problem);
    }

    [Fact]
    public async Task AChangeThatCantBeSavedSaysSoAndShowsTheSettingsAsStored()
    {
        await Agents.LoadAsync(Cancellation);
        _harness.Settings.SaveRefusal = new IOException("The disk is full.");

        Agents.IsEnabled = true;
        await Agents.Changing;

        Assert.Equal("The change couldn't be saved: The disk is full.", Agents.Problem);
        Assert.False(Agents.IsEnabled);
        Assert.Equal([(false, AppSettings.DefaultAgentsPort)], _harness.AgentServer.Applied);
        Assert.Single(_harness.AgentsLogger.Entries);
    }

    [Fact]
    public async Task SettingsThatCantBeReadLeaveAgentsOff()
    {
        _harness.Settings.Stored = new AppSettings { AgentsEnabled = true };
        _harness.Settings.ReadRefusal = new InvalidDataException("settings.json isn't JSON.");

        await Agents.LoadAsync(Cancellation);

        Assert.False(Agents.IsEnabled);
        Assert.Equal([(false, AppSettings.DefaultAgentsPort)], _harness.AgentServer.Applied);
    }

    [Theory]
    [InlineData(AgentServerStatus.Starting, null, "Starting…", false)]
    [InlineData(AgentServerStatus.PortInUse, null, "Port 50000 is in use by another app. Choose another port, then Save.", true)]
    [InlineData(AgentServerStatus.NoCommand, null, "Can't be served: the command line, ModShelf.Cli, isn't beside the app.", true)]
    [InlineData(AgentServerStatus.Failed, 1, "Stopped, with exit code 1; the log says why. Turn agents off and on to try again.", true)]
    [InlineData(AgentServerStatus.Failed, null, "Didn't start in time; the log says why. Turn agents off and on to try again.", true)]
    [InlineData(AgentServerStatus.Running, null, "Served at http://127.0.0.1:50000/mcp while ModShelf runs.", false)]
    public async Task SaysWhereTheAddressStandsAsItChanges(AgentServerStatus status, int? exitCode, string said, bool problem)
    {
        _harness.Settings.Stored = new AppSettings { AgentsPort = 50000 };
        await Agents.LoadAsync(Cancellation);
        _harness.UiThread.Inline = false;

        _harness.AgentServer.Show(new AgentServerState(status, 50000, exitCode));

        Assert.Equal("Not served: agents are off.", Agents.Status);
        _harness.UiThread.RunPending();
        Assert.Equal((said, problem), (Agents.Status, Agents.HasStatusProblem));
    }

    [Fact]
    public async Task SavesThePortTypedAndServesTheAddressThere()
    {
        _harness.Settings.Stored = new AppSettings { AgentsEnabled = true };
        await Agents.LoadAsync(Cancellation);
        Assert.False(Agents.SavePortCommand.CanExecute(null));

        Agents.PortText = " 48000 ";
        Assert.True(Agents.SavePortCommand.CanExecute(null));
        await Agents.SavePortCommand.ExecuteAsync(null);

        Assert.Equal(48000, _harness.Settings.Stored.AgentsPort);
        Assert.Equal((true, 48000), _harness.AgentServer.Applied[^1]);
        Assert.Equal(("48000", "Served at http://127.0.0.1:48000/mcp while ModShelf runs."), (Agents.PortText, Agents.Status));
        Assert.False(Agents.SavePortCommand.CanExecute(null));

        // The default port is left out of the settings.
        Agents.PortText = "47823";
        await Agents.SavePortCommand.ExecuteAsync(null);

        Assert.Equal(["agentsPort 48000", "agentsPort null"], _harness.Settings.Changes);
        Assert.Equal((true, AppSettings.DefaultAgentsPort), _harness.AgentServer.Applied[^1]);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("65536")]
    [InlineData("port")]
    [InlineData("")]
    public async Task APortOutOfRangeIsntSaved(string typed)
    {
        await Agents.LoadAsync(Cancellation);

        Agents.PortText = typed;
        Assert.False(Agents.SavePortCommand.CanExecute(null));
        await Agents.SavePortCommand.ExecuteAsync(null);

        Assert.Equal("The port can be from 1024 to 65535.", Agents.Problem);
        Assert.Empty(_harness.Settings.Changes);
        Assert.Single(_harness.AgentServer.Applied);
    }

    [Fact]
    public async Task CopiesAPromptThatNamesTheCommandAndTheAddress()
    {
        _harness.Settings.Stored = new AppSettings { AgentsEnabled = true };
        await Agents.LoadAsync(Cancellation);

        await Agents.CopyPromptCommand.ExecuteAsync(null);

        Assert.Equal(
            "Add a local MCP server named \"modshelf\" to my user-level MCP configuration on this PC. It starts the command "
            + $"\"{Installed}\" with the argument \"mcp\". If you can only add servers by address, add a Streamable HTTP MCP "
            + "server named \"modshelf\" at http://127.0.0.1:47823/mcp instead, which works while the ModShelf app runs. If you "
            + "cannot configure MCP servers directly, show me where to add it manually.",
            Assert.Single(_harness.Clipboard.Texts));
        Assert.Equal("The prompt is copied: paste it into your agent's chat.", Agents.Done);
    }

    [Fact]
    public async Task ThePromptNamesTheLibrarysEnvironmentAndThePortAsSaved()
    {
        _harness.Settings.Stored = new AppSettings { AgentsPort = 50000 };
        _harness.AgentServer.Command = ScriptedAgentServer.Installed with
        {
            Environment = new Dictionary<string, string>
            {
                ["ModShelf__SecretScope"] = "ModShelf.Checklist",
                ["ModShelf__DatabasePath"] = @"D:\Mods test\library.db",
            },
        };
        await Agents.LoadAsync(Cancellation);

        await Agents.CopyPromptCommand.ExecuteAsync(null);

        string prompt = Assert.Single(_harness.Clipboard.Texts);
        Assert.Contains(
            "with the argument \"mcp\" and these environment variables: ModShelf__DatabasePath=D:\\Mods test\\library.db, "
            + "ModShelf__SecretScope=ModShelf.Checklist. If you can only add servers by address, add a Streamable HTTP MCP "
            + "server named \"modshelf\" at http://127.0.0.1:50000/mcp instead",
            prompt,
            StringComparison.Ordinal);

        // Agents are off: the prompt still works once they're on.
        Assert.Equal(
            "The prompt is copied: paste it into your agent's chat. Turn on Allow agents to use ModShelf too, or its calls are refused.",
            Agents.Done);
    }

    [Fact]
    public async Task WithoutACommandThePromptNamesTheAddressAlone()
    {
        _harness.AgentServer.Command = null;
        await Agents.LoadAsync(Cancellation);

        await Agents.CopyPromptCommand.ExecuteAsync(null);

        Assert.Equal(
            "Add a Streamable HTTP MCP server named \"modshelf\" at http://127.0.0.1:47823/mcp to my user-level MCP configuration "
            + "on this PC. It works while the ModShelf app runs. If you cannot configure MCP servers directly, show me where "
            + "to add it manually.",
            Assert.Single(_harness.Clipboard.Texts));
        Assert.StartsWith("Not found: the command line, ModShelf.Cli, isn't beside the app", Agents.CommandNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClipboardThatRefusesIsSaid()
    {
        await Agents.LoadAsync(Cancellation);
        _harness.Clipboard.Refusal = new InvalidOperationException("The clipboard is busy.");

        await Agents.CopyPromptCommand.ExecuteAsync(null);

        Assert.Equal(("Something went wrong. The log has the details.", (string?)null), (Agents.Problem, Agents.Done));
        Assert.Single(_harness.AgentsLogger.Entries);
    }

    [Fact]
    public async Task ShowsTheDetailsEachToCopy()
    {
        await Agents.LoadAsync(Cancellation);

        await Agents.ShowDetailsCommand.ExecuteAsync(null);

        AgentDetailsViewModel details = Assert.Single(_harness.Dialogs.AgentDetails);
        Assert.Equal(
            [
                "Name",
                "Command",
                "Arguments",
                "Address (Streamable HTTP)",
                "Claude Desktop: claude_desktop_config.json (Settings, Developer, Edit Config)",
                "Claude Code: with the command",
                "Claude Code: with the address",
            ],
            details.Details.Select(detail => detail.Label));
        Assert.Equal(
            ["modshelf", Installed, "mcp", "http://127.0.0.1:47823/mcp"],
            details.Details.Take(4).Select(detail => detail.Value));
        Assert.Equal(
            $"claude mcp add --transport stdio --scope user modshelf -- {Installed} mcp",
            details.Details[5].Value);
        Assert.Equal("claude mcp add --transport http --scope user modshelf http://127.0.0.1:47823/mcp", details.Details[6].Value);
        Assert.All(details.Details, detail => Assert.True(detail.IsCode));
        Assert.Equal((Agents.Status, (string?)null), (details.Status, details.NoCommand));

        AgentDetail command = details.Details[1];
        Assert.False(command.IsCopied);
        await command.CopyCommand.ExecuteAsync(null);

        Assert.Equal([Installed], _harness.Clipboard.Texts);
        Assert.True(command.IsCopied);
    }

    [Fact]
    public async Task WithoutACommandTheDetailsGiveTheAddress()
    {
        _harness.AgentServer.Command = null;
        await Agents.LoadAsync(Cancellation);

        await Agents.ShowDetailsCommand.ExecuteAsync(null);

        AgentDetailsViewModel details = Assert.Single(_harness.Dialogs.AgentDetails);
        Assert.Equal(["Name", "Address (Streamable HTTP)", "Claude Code: with the address"], details.Details.Select(detail => detail.Label));
        Assert.StartsWith("There's no command for agents' apps to start", details.NoCommand, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ClaudeDesktopServer.Absent, "Claude Desktop doesn't have ModShelf yet.", true)]
    [InlineData(ClaudeDesktopServer.Different, "Claude Desktop has a server named modshelf that starts something else.", true)]
    [InlineData(ClaudeDesktopServer.Same, "Claude Desktop has ModShelf.", false)]
    [InlineData(ClaudeDesktopServer.NotFound, "Claude Desktop wasn't found on this computer. Start it once, then come back.", false)]
    public async Task SaysWhetherClaudeDesktopHasModShelf(ClaudeDesktopServer found, string said, bool canAdd)
    {
        _harness.ClaudeDesktop.Found = found;

        await Agents.LoadAsync(Cancellation);

        Assert.Equal((said, canAdd, canAdd), (Agents.ClaudeDesktopNote, Agents.CanAddToClaudeDesktop, Agents.AddToClaudeDesktopCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ShownAgainTheSettingsScreenLooksAgainAtClaudeDesktop()
    {
        _harness.ClaudeDesktop.Found = ClaudeDesktopServer.NotFound;
        await Agents.LoadAsync(Cancellation);
        _harness.ClaudeDesktop.Found = ClaudeDesktopServer.Absent;

        _harness.Navigation.NavigateTo(Screen.Library);
        await Agents.Refreshing;
        Assert.False(Agents.CanAddToClaudeDesktop);

        _harness.Navigation.NavigateTo(Screen.Settings);
        await Agents.Refreshing;

        Assert.Equal(("Claude Desktop doesn't have ModShelf yet.", true), (Agents.ClaudeDesktopNote, Agents.CanAddToClaudeDesktop));
    }

    [Fact]
    public async Task AddsModShelfToClaudeDesktopAfterAsking()
    {
        await Agents.LoadAsync(Cancellation);
        _harness.Dialogs.Confirm.Enqueue(true);

        await Agents.AddToClaudeDesktopCommand.ExecuteAsync(null);

        (string title, string message, string confirm) = Assert.Single(_harness.Dialogs.Confirmations);
        Assert.Equal(("Add ModShelf to Claude Desktop?", "Add"), (title, confirm));
        Assert.Equal(
            $"ModShelf adds its MCP server to Claude Desktop's configuration, {_harness.ClaudeDesktop.FilePath}, keeping its "
            + $"other servers and settings, and keeps a copy of the file as it was, {_harness.ClaudeDesktop.BackupPath}.",
            message);
        Assert.Equal(ScriptedAgentServer.Installed, Assert.Single(_harness.ClaudeDesktop.Added));
        Assert.Equal(
            ("Added to Claude Desktop", "Quit Claude Desktop completely, from its menu or the tray, and start it again: ModShelf's tools are then in its chats. Turn on Allow agents to use ModShelf too, or their calls are refused."),
            Assert.Single(_harness.Dialogs.Messages));
        Assert.Equal(("Claude Desktop has ModShelf.", false), (Agents.ClaudeDesktopNote, Agents.CanAddToClaudeDesktop));
    }

    [Fact]
    public async Task ReplacesAnotherServerByItsNameAfterAsking()
    {
        _harness.Settings.Stored = new AppSettings { AgentsEnabled = true };
        _harness.ClaudeDesktop.Found = ClaudeDesktopServer.Different;
        await Agents.LoadAsync(Cancellation);
        _harness.Dialogs.Confirm.Enqueue(true);

        await Agents.AddToClaudeDesktopCommand.ExecuteAsync(null);

        (string title, string message, string confirm) = Assert.Single(_harness.Dialogs.Confirmations);
        Assert.Equal(("Replace modshelf in Claude Desktop?", "Replace"), (title, confirm));
        Assert.StartsWith($"Claude Desktop's configuration, {_harness.ClaudeDesktop.FilePath}, has a server named modshelf", message, StringComparison.Ordinal);
        Assert.Single(_harness.ClaudeDesktop.Added);
        Assert.Equal(
            ("Added to Claude Desktop", "Quit Claude Desktop completely, from its menu or the tray, and start it again: ModShelf's tools are then in its chats."),
            Assert.Single(_harness.Dialogs.Messages));
    }

    [Fact]
    public async Task NothingIsAddedWhenTheUserSaysNo()
    {
        await Agents.LoadAsync(Cancellation);
        _harness.Dialogs.Confirm.Enqueue(false);

        await Agents.AddToClaudeDesktopCommand.ExecuteAsync(null);

        Assert.Single(_harness.Dialogs.Confirmations);
        Assert.Empty(_harness.ClaudeDesktop.Added);
        Assert.Empty(_harness.Dialogs.Messages);
    }

    [Theory]
    [InlineData(ClaudeDesktopServer.Same, "Claude Desktop has ModShelf", "Claude Desktop has ModShelf.")]
    [InlineData(ClaudeDesktopServer.NotFound, "Can't add ModShelf to Claude Desktop", "Claude Desktop wasn't found on this computer. Start it once, then come back.")]
    public async Task SaysWhyNothingIsAddedWhenClaudeDesktopChangedMeanwhile(ClaudeDesktopServer found, string title, string note)
    {
        await Agents.LoadAsync(Cancellation);
        _harness.ClaudeDesktop.Found = found;

        await Agents.AddToClaudeDesktopCommand.ExecuteAsync(null);

        Assert.Equal(title, Assert.Single(_harness.Dialogs.Messages).Title);
        Assert.Empty(_harness.Dialogs.Confirmations);
        Assert.Empty(_harness.ClaudeDesktop.Added);
        Assert.Equal((note, false), (Agents.ClaudeDesktopNote, Agents.CanAddToClaudeDesktop));
    }

    [Fact]
    public async Task AConfigurationThatCantBeReadOrWrittenIsSaid()
    {
        _harness.ClaudeDesktop.ReadFails = new InvalidDataException("claude_desktop_config.json can't be read: it isn't JSON.");

        await Agents.LoadAsync(Cancellation);

        Assert.Equal(
            ("Claude Desktop's configuration can't be read: claude_desktop_config.json can't be read: it isn't JSON.", false),
            (Agents.ClaudeDesktopNote, Agents.CanAddToClaudeDesktop));

        _harness.ClaudeDesktop.ReadFails = null;
        _harness.ClaudeDesktop.AddFails = new UnauthorizedAccessException("Access to the path is denied.");
        _harness.Dialogs.Confirm.Enqueue(true);
        await Agents.AddToClaudeDesktopCommand.ExecuteAsync(null);

        Assert.Equal(("Can't add ModShelf to Claude Desktop", "Access to the path is denied."), Assert.Single(_harness.Dialogs.Messages));
        Assert.Empty(_harness.ClaudeDesktop.Added);
    }

    [Fact]
    public async Task WithoutACommandClaudeDesktopCantStartModShelf()
    {
        _harness.AgentServer.Command = null;

        await Agents.LoadAsync(Cancellation);

        Assert.Equal(
            ("Claude Desktop starts its servers by command, and there's none here: the command line isn't beside the app.", false),
            (Agents.ClaudeDesktopNote, Agents.CanAddToClaudeDesktop));
    }
}
