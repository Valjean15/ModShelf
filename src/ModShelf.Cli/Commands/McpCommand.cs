using System.CommandLine;

using ModShelf.Cli.Hosting;
using ModShelf.Cli.Mcp;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>mcp</c>: runs ModShelf's MCP server on standard input and output, for an AI agent's app to start (ADR 0040).
/// </summary>
internal static class McpCommand
{
    public static Command Create(CliEnvironment environment)
    {
        Command mcp = new(
            "mcp",
            "Runs ModShelf's MCP server on standard input and output, for an AI agent: it lists the library, the updates, "
            + "the downloads and the settings, downloads files into the library, and changes the queue and the settings. "
            + "It never links accounts, deletes files or moves the library. An agent's app, such as Claude Desktop, starts it.");
        mcp.SetAction((_, cancellationToken) => ModShelfMcpServer.RunAsync(environment, cancellationToken));
        return mcp;
    }
}
