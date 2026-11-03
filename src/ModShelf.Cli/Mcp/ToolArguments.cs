using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// Says what a tool takes when the arguments an agent gives it don't fit, rather than only that it failed. An agent's app
/// keeps the list of tools it read when it started, so after ModShelf is updated, it may call a tool as an older ModShelf
/// had it, such as <c>download</c> with one <c>link</c>, which takes <c>links</c> since 0.9.3.
/// </summary>
internal static class ToolArguments
{
    /// <summary>
    /// Wraps the call of a tool: arguments its input schema doesn't allow, a required one missing or one of another type,
    /// are the tool's error, said in full, and the tool isn't called. Without this, the SDK fails to bind them, and says
    /// only that an error occurred.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Explained(McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async (request, cancellationToken) =>
        {
            if (request.MatchedPrimitive is McpServerTool tool
                && ProblemWith(tool.ProtocolTool.InputSchema, request.Params?.Arguments) is { } problem)
            {
                throw new McpException(Describe(tool.ProtocolTool, request.Params?.Arguments?.Keys, problem));
            }

            return await next(request, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// What's wrong with <paramref name="arguments"/>, as <paramref name="schema"/> has them: such as "it needs links", or
    /// "limit is an integer, not a string"; null when nothing is.
    /// </summary>
    internal static string? ProblemWith(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (string name in required.EnumerateArray().Select(name => name.GetString()).OfType<string>())
            {
                if (arguments is null || !arguments.ContainsKey(name))
                {
                    return $"it needs {name}";
                }
            }
        }

        if (arguments is null || !schema.TryGetProperty("properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach ((string name, JsonElement value) in arguments)
        {
            if (properties.TryGetProperty(name, out JsonElement property) && !Fits(value, property))
            {
                return $"{name} is {Expected(property)}, not {KindOf(value)}";
            }
        }

        return null;
    }

    /// <summary>
    /// Such as "download doesn't take these arguments: it needs links. It takes links (required). It was given link,
    /// which it doesn't take. ...".
    /// </summary>
    internal static string Describe(Tool tool, IEnumerable<string>? given, string problem)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(problem);

        JsonElement schema = tool.InputSchema;
        HashSet<string> required = schema.TryGetProperty("required", out JsonElement names) && names.ValueKind == JsonValueKind.Array
            ? [.. names.EnumerateArray().Select(name => name.GetString()).OfType<string>()]
            : [];
        bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object;
        string[] takes = hasProperties
            ? [.. properties.EnumerateObject().Select(property => required.Contains(property.Name) ? $"{property.Name} (required)" : property.Name)]
            : [];
        string[] unknown = [.. (given ?? []).Where(name => !hasProperties || !properties.TryGetProperty(name, out _))];

        string message = $"{tool.Name} doesn't take these arguments: {problem.TrimEnd('.')}.";
        message += takes.Length == 0 ? " It takes none." : $" It takes {string.Join(", ", takes)}.";
        if (unknown.Length > 0)
        {
            message += $" It was given {string.Join(", ", unknown)}, which it doesn't take.";
        }

        return message
            + " The agent's list of ModShelf's tools may be older than this MCP server: restarting the agent's app, then "
            + "starting a new conversation, reads it again.";
    }

    /// <summary>
    /// Whether <paramref name="value"/> is of a type <paramref name="schema"/> allows, and so is each item of a list; a
    /// schema that names no type allows any.
    /// </summary>
    private static bool Fits(JsonElement value, JsonElement schema)
    {
        string[] types = TypesOf(schema);
        if (types.Length == 0)
        {
            return true;
        }

        return types.Any(type => type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array
                && (!schema.TryGetProperty("items", out JsonElement items) || value.EnumerateArray().All(item => Fits(item, items))),
            _ => true,
        });
    }

    /// <summary>
    /// Such as "a string", "a list of strings", or "an integer or null".
    /// </summary>
    private static string Expected(JsonElement schema)
        => string.Join(" or ", TypesOf(schema).Select(type => type switch
        {
            "string" => "a string",
            "integer" => "an integer",
            "number" => "a number",
            "boolean" => "true or false",
            "null" => "null",
            "object" => "an object",
            "array" when schema.TryGetProperty("items", out JsonElement items) && TypesOf(items).Where(item => item != "null").SequenceEqual(["string"]) => "a list of strings",
            "array" => "a list",
            _ => type,
        }));

    private static string KindOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        JsonValueKind.Null => "null",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        _ => "something else",
    };

    /// <summary>
    /// The types a schema allows, as JSON Schema names them: one, or several in a list.
    /// </summary>
    private static string[] TypesOf(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out JsonElement type))
        {
            return [];
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => [type.GetString()!],
            JsonValueKind.Array => [.. type.EnumerateArray().Select(name => name.GetString()).OfType<string>()],
            _ => [],
        };
    }
}
