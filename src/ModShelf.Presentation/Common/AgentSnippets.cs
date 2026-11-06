using System.Text.Json;
using System.Text.Json.Nodes;

using ModShelf.Application.Agents;
using ModShelf.Application.Agents.Ports;
using ModShelf.Presentation.Resources;

namespace ModShelf.Presentation.Common;

/// <summary>
/// What the user copies to connect an agent to ModShelf's MCP server: a prompt for the agent itself, Claude Desktop's
/// configuration, and Claude Code's commands (ADR 0080).
/// </summary>
internal static class AgentSnippets
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// A prompt that asks an agent to add the server to its app's configuration: the command its app starts, with the
    /// address as the other way; the address alone when there's no command.
    /// </summary>
    public static string Prompt(AgentCommand? command, Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return command is null
            ? Text.Format(Strings.AgentsPromptAddress, AgentAddress.ServerName, address)
            : Text.Format(
                command.Environment.Count == 0 ? Strings.AgentsPromptCommand : Strings.AgentsPromptCommandWithEnvironment,
                AgentAddress.ServerName,
                command.Path,
                string.Join(' ', command.Arguments),
                address,
                string.Join(", ", Variables(command)));
    }

    /// <summary>
    /// Claude Desktop's <c>claude_desktop_config.json</c> with the server alone.
    /// </summary>
    public static string ClaudeDesktop(AgentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        JsonObject server = new()
        {
            ["command"] = command.Path,
            ["args"] = new JsonArray([.. command.Arguments.Select(argument => (JsonNode?)JsonValue.Create(argument))]),
        };
        if (command.Environment.Count > 0)
        {
            server["env"] = new JsonObject(command.Environment
                .OrderBy(variable => variable.Key, StringComparer.Ordinal)
                .Select(variable => KeyValuePair.Create(variable.Key, (JsonNode?)JsonValue.Create(variable.Value))));
        }

        return new JsonObject { ["mcpServers"] = new JsonObject { [AgentAddress.ServerName] = server } }.ToJsonString(Indented);
    }

    /// <summary>
    /// Claude Code's command that adds the server for every project of the user's, as a command it starts.
    /// </summary>
    public static string ClaudeCodeCommand(AgentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // An --env just before the name would take the name for another variable, so --transport comes between them.
        IEnumerable<string> environment = Variables(command).Select(variable => $"--env {Quoted(variable)} ");
        return $"claude mcp add {string.Concat(environment)}--transport stdio --scope user {AgentAddress.ServerName} -- {Quoted(command.Path)} {string.Join(' ', command.Arguments)}";
    }

    /// <summary>
    /// Claude Code's command that adds the server for every project of the user's, by its address.
    /// </summary>
    public static string ClaudeCodeAddress(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return $"claude mcp add --transport http --scope user {AgentAddress.ServerName} {address}";
    }

    /// <summary>
    /// Each environment variable as <c>name=value</c>, by name.
    /// </summary>
    internal static IEnumerable<string> Variables(AgentCommand command)
        => command.Environment.OrderBy(variable => variable.Key, StringComparer.Ordinal).Select(variable => $"{variable.Key}={variable.Value}");

    /// <summary>
    /// In double quotes, which PowerShell, cmd and a Unix shell all take, when it holds a space or a quote.
    /// </summary>
    private static string Quoted(string text)
        => text.Any(character => char.IsWhiteSpace(character) || character is '"' or '\'') ? $"\"{text.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : text;
}
