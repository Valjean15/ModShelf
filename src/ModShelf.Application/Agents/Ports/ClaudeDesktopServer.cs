namespace ModShelf.Application.Agents.Ports;

/// <summary>
/// Whether Claude Desktop's configuration has ModShelf's MCP server.
/// </summary>
public enum ClaudeDesktopServer
{
    /// <summary>Claude Desktop isn't on this computer, or hasn't run yet: its folder isn't there.</summary>
    NotFound,

    /// <summary>Its configuration has no server named <c>modshelf</c>.</summary>
    Absent,

    /// <summary>It has ModShelf's, as it would be added.</summary>
    Same,

    /// <summary>It has a server named <c>modshelf</c> that starts something else, or in another way.</summary>
    Different,
}
