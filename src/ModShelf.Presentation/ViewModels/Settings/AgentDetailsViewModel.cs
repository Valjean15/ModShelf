using ModShelf.Application.Agents;
using ModShelf.Application.Agents.Ports;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// The MCP server's details, for connecting an agent's app by hand: its name, the command its app starts, the address,
/// and Claude Desktop's and Claude Code's configuration, each to copy.
/// </summary>
public sealed class AgentDetailsViewModel
{
    public AgentDetailsViewModel(AgentCommand? command, Uri address, string status, IClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(clipboard);

        Status = status;
        List<AgentDetail> details = [new(Strings.AgentsDetailName, AgentAddress.ServerName, isCode: true, clipboard)];
        if (command is not null)
        {
            details.Add(new(Strings.AgentsDetailCommand, command.Path, isCode: true, clipboard));
            details.Add(new(Strings.AgentsDetailArguments, string.Join(' ', command.Arguments), isCode: true, clipboard));
            if (command.Environment.Count > 0)
            {
                details.Add(new(Strings.AgentsDetailEnvironment, string.Join(Environment.NewLine, AgentSnippets.Variables(command)), isCode: true, clipboard));
            }
        }

        details.Add(new(Strings.AgentsDetailAddress, address.ToString(), isCode: true, clipboard));
        if (command is not null)
        {
            details.Add(new(Strings.AgentsDetailClaudeDesktop, AgentSnippets.ClaudeDesktop(command), isCode: true, clipboard));
            details.Add(new(Strings.AgentsDetailClaudeCode, AgentSnippets.ClaudeCodeCommand(command), isCode: true, clipboard));
        }

        details.Add(new(Strings.AgentsDetailClaudeCodeAddress, AgentSnippets.ClaudeCodeAddress(address), isCode: true, clipboard));
        Details = details;
        NoCommand = command is null ? Strings.AgentsDetailsNoCommand : null;
    }

    /// <summary>
    /// Where the address stands, as Settings › Agents says it.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Why there's no command to start; null when there is.
    /// </summary>
    public string? NoCommand { get; }

    public IReadOnlyList<AgentDetail> Details { get; }
}
