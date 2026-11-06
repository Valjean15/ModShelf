using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Settings;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// Whether agents may use ModShelf, which the user turns on in the desktop app or with the CLI (<c>agents.enabled</c>).
/// While they may not, the server still starts and lists its tools, so an agent can say what to do, and every call is
/// refused, saying so; the setting is read at each call, so turning it on takes effect without restarting the agent's
/// app (ADR 0080).
/// </summary>
internal static partial class AgentAccess
{
    /// <summary>
    /// What a call is answered while agents may not use ModShelf.
    /// </summary>
    internal const string Off =
        "AI agents are off in ModShelf: the user hasn't allowed them to use it. Ask the user to turn on \"Allow agents to "
        + "use ModShelf\" in ModShelf's Settings, Agents, then call again.";

    /// <summary>
    /// Wraps the call of a tool: it goes on only while agents may use ModShelf.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Checked(
        ISettingsService settings,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        return next => async (request, cancellationToken) =>
        {
            bool allowed;
            try
            {
                allowed = (await settings.GetAsync(cancellationToken).ConfigureAwait(false)).AgentsEnabled ?? false;
            }
            catch (InvalidDataException exception)
            {
                LogSettingsUnread(logger, exception);
                throw new McpException(
                    $"ModShelf's settings can't be read, so AI agents count as off: {exception.Message} Ask the user to fix them.",
                    exception);
            }

            if (!allowed)
            {
                LogRefused(logger, request.Params?.Name);
                throw new McpException(Off);
            }

            return await next(request, cancellationToken).ConfigureAwait(false);
        };
    }
}
