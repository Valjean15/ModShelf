using System.Globalization;

namespace ModShelf.Application.Agents;

/// <summary>
/// The address the desktop app serves ModShelf's MCP server at while agents may use ModShelf, on this computer alone,
/// such as <c>http://127.0.0.1:47823/mcp</c>; and the server's name in the agents' apps.
/// </summary>
/// <param name="Port">The address's port.</param>
public sealed record AgentAddress(int Port)
{
    /// <summary>
    /// The server's name, in the agents' apps and in its own answers.
    /// </summary>
    public const string ServerName = "modshelf";

    /// <summary>
    /// The address's path.
    /// </summary>
    public const string Path = "/mcp";

    /// <summary>
    /// The address itself.
    /// </summary>
    public Uri Uri => new(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Port}{Path}"));

    /// <summary>
    /// The address at <paramref name="port"/>.
    /// </summary>
    public static Uri Of(int port) => new AgentAddress(port).Uri;
}
