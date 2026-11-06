using System.CommandLine;

using ModShelf.Application.Settings;
using ModShelf.Cli.Hosting;
using ModShelf.Cli.Mcp;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mcp</c>: runs ModShelf's MCP server on standard input and output, for an AI agent's app to start (ADR 0040); with
/// <c>--http</c>, at its address on this computer, for apps that reach servers by address (ADR 0080).
/// </summary>
internal static class McpCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Option<bool> http = new("--http")
        {
            Description = "Serves it at http://127.0.0.1:<port>/mcp, Streamable HTTP, for this computer's apps alone, until "
                + "stopped, rather than on standard input and output.",
        };
        Option<int?> port = new("--port")
        {
            Description = $"With --http, the port: from {AppSettings.MinAgentsPort} to {AppSettings.MaxAgentsPort}, or 0 for any free "
                + $"one, which the output says; agents.port's, {AppSettings.DefaultAgentsPort} by default, when left out.",
        };
        port.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is { } value && value != 0 && value is < AppSettings.MinAgentsPort or > AppSettings.MaxAgentsPort)
            {
                result.AddError($"The port can be from {AppSettings.MinAgentsPort} to {AppSettings.MaxAgentsPort}, or 0 for any free one.");
            }
        });
        Option<int?> parent = new("--parent")
        {
            Description = "With --http, the ID of a process, such as the desktop app's, whose end ends the server too.",
        };

        Command mcp = new(
            "mcp",
            "Runs ModShelf's MCP server on standard input and output, for an AI agent: it lists the library, the updates, "
            + "the downloads and the settings, downloads files into the library, and changes the queue and the settings. "
            + "It never links accounts, deletes files or moves the library. An agent's app, such as Claude Desktop, starts it. "
            + "Its tools answer only while agents.enabled is true, which the user turns on.")
        {
            http,
            port,
            parent,
        };
        mcp.SetAction((result, cancellationToken) => result.GetValue(http)
            ? ModShelfMcpServer.RunHttpAsync(environment, result.GetValue(port), result.GetValue(parent), cancellationToken)
            : ModShelfMcpServer.RunAsync(environment, cancellationToken));
        return mcp;
    }
}
