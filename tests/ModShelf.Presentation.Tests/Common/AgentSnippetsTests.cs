using ModShelf.Application.Agents.Ports;
using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.Tests.Common;

/// <summary>
/// What the user copies to connect an agent: Claude Desktop's configuration and Claude Code's commands, as each reads
/// them.
/// </summary>
public sealed class AgentSnippetsTests
{
    private static readonly AgentCommand OnMacOS = new("/Applications/ModShelf.app/Contents/MacOS/ModShelf.Cli", ["mcp"], new Dictionary<string, string>());

    private static readonly AgentCommand WithSpaces = new(
        @"D:\Apps\Mod Shelf\ModShelf.Cli.exe",
        ["mcp"],
        new Dictionary<string, string>
        {
            ["ModShelf__SecretScope"] = "ModShelf.Checklist",
            ["ModShelf__DatabasePath"] = @"D:\Mods test\library.db",
        });

    [Fact]
    public void ClaudeDesktopsConfigurationHasTheServerAlone()
    {
        Assert.Equal(
            """
            {
              "mcpServers": {
                "modshelf": {
                  "command": "/Applications/ModShelf.app/Contents/MacOS/ModShelf.Cli",
                  "args": [
                    "mcp"
                  ]
                }
              }
            }
            """.ReplaceLineEndings(),
            AgentSnippets.ClaudeDesktop(OnMacOS).ReplaceLineEndings());
    }

    [Fact]
    public void ClaudeDesktopsConfigurationHasTheLibrarysEnvironmentByName()
    {
        string json = AgentSnippets.ClaudeDesktop(WithSpaces);

        Assert.Contains(
            """
                  "env": {
                    "ModShelf__DatabasePath": "D:\\Mods test\\library.db",
                    "ModShelf__SecretScope": "ModShelf.Checklist"
                  }
            """.ReplaceLineEndings(),
            json.ReplaceLineEndings(),
            StringComparison.Ordinal);
        Assert.Contains("\"command\": \"D:\\\\Apps\\\\Mod Shelf\\\\ModShelf.Cli.exe\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeCodesCommandQuotesWhatHasSpacesAndPutsTheEnvironmentFirst()
    {
        Assert.Equal(
            "claude mcp add --transport stdio --scope user modshelf -- /Applications/ModShelf.app/Contents/MacOS/ModShelf.Cli mcp",
            AgentSnippets.ClaudeCodeCommand(OnMacOS));

        // An --env just before the name would take it for another variable.
        Assert.Equal(
            "claude mcp add --env \"ModShelf__DatabasePath=D:\\Mods test\\library.db\" --env ModShelf__SecretScope=ModShelf.Checklist "
            + "--transport stdio --scope user modshelf -- \"D:\\Apps\\Mod Shelf\\ModShelf.Cli.exe\" mcp",
            AgentSnippets.ClaudeCodeCommand(WithSpaces));
    }

    [Fact]
    public void ClaudeCodesCommandForTheAddress()
    {
        Assert.Equal(
            "claude mcp add --transport http --scope user modshelf http://127.0.0.1:50000/mcp",
            AgentSnippets.ClaudeCodeAddress(new Uri("http://127.0.0.1:50000/mcp")));
    }
}
