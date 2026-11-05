using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using ModShelf.Application.Diagnostics;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// What a call that uses a deprecated argument gets, the same for every tool: a warning in the log, the call counted for
/// the diagnostics report, the <c>deprecated</c> block for its result, and the note its message starts with.
/// </summary>
internal sealed partial class McpDeprecations(IDeprecatedCalls calls, ILogger<McpDeprecations> logger)
{
    /// <summary>
    /// Says the call used <paramref name="deprecation"/>'s argument: warns in the log, with the client's name but none of
    /// the arguments, and counts the call.
    /// </summary>
    /// <param name="example">The call's arguments as they should be given, with the caller's own values.</param>
    /// <returns>The block for the call's result.</returns>
    public async Task<DeprecatedArgument<TExample>> NoteAsync<TExample>(
        Deprecation deprecation, TExample example, McpServer? server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deprecation);

        string? client = server?.ClientInfo?.Name;
        LogDeprecatedArgument(logger, deprecation.Code, deprecation.Tool, deprecation.Argument, deprecation.Use, client ?? "unknown");
        await calls.CountAsync(deprecation.Name, deprecation.Code, client, cancellationToken).ConfigureAwait(false);
        return new DeprecatedArgument<TExample>(deprecation.Code, deprecation.Argument, deprecation.Use, deprecation.RemovedIn, example);
    }

    /// <summary>
    /// The result's <paramref name="message"/>, after the note on <paramref name="deprecation"/>.
    /// </summary>
    public static string Message(Deprecation deprecation, string message)
    {
        ArgumentNullException.ThrowIfNull(deprecation);

        return $"Note: {deprecation.Note} {message}";
    }

    /// <summary>
    /// Marks the input schema of each of <paramref name="method"/>'s arguments with <see cref="DeprecatedAttribute"/>
    /// <c>"deprecated": true</c>, as JSON Schema has it, for clients that hide or warn of deprecated arguments.
    /// </summary>
    /// <param name="tool">The tool made from <paramref name="method"/>.</param>
    public static void MarkDeprecated(Tool tool, MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(method);

        string[] deprecated = [.. method.GetParameters().Where(parameter => parameter.IsDefined(typeof(DeprecatedAttribute), inherit: false)).Select(parameter => parameter.Name!)];
        if (deprecated.Length == 0)
        {
            return;
        }

        JsonObject schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
        foreach (string name in deprecated)
        {
            if (schema["properties"]?[name] is JsonObject argument)
            {
                argument["deprecated"] = true;
            }
        }

        tool.InputSchema = JsonSerializer.SerializeToElement(schema);
    }
}
