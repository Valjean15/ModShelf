using System.Text.Json.Nodes;

namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// Checks a tool's result against its output schema as clients that check results do: each required property is there,
/// and each value is of a type the schema allows. Only the parts of JSON Schema the tools' schemas use.
/// </summary>
internal static class OutputSchemas
{
    /// <returns>Each way <paramref name="value"/> doesn't fit <paramref name="schema"/>, as a path and what's wrong.</returns>
    public static IReadOnlyList<string> Problems(JsonNode? value, JsonNode schema, string path = "data")
    {
        ArgumentNullException.ThrowIfNull(schema);

        List<string> problems = [];
        string[] types = schema["type"] switch
        {
            JsonValue one => [one.GetValue<string>()],
            JsonArray many => [.. many.Select(type => type!.GetValue<string>())],
            _ => [],
        };
        if (types.Length > 0 && !types.Any(type => Is(value, type)))
        {
            problems.Add($"{path} isn't {string.Join(" or ", types)}");
            return problems;
        }

        if (value is JsonObject properties)
        {
            foreach (JsonNode? required in schema["required"]?.AsArray() ?? [])
            {
                if (!properties.ContainsKey(required!.GetValue<string>()))
                {
                    problems.Add($"{path} must have required property '{required.GetValue<string>()}'");
                }
            }

            if (schema["properties"] is JsonObject known)
            {
                foreach ((string name, JsonNode? property) in known)
                {
                    if (properties.TryGetPropertyValue(name, out JsonNode? inner))
                    {
                        problems.AddRange(Problems(inner, property!, $"{path}/{name}"));
                    }
                }
            }
        }

        if (value is JsonArray items && schema["items"] is JsonNode itemSchema)
        {
            for (int index = 0; index < items.Count; index++)
            {
                problems.AddRange(Problems(items[index], itemSchema, $"{path}/{index}"));
            }
        }

        return problems;
    }

    private static bool Is(JsonNode? value, string type) => (type, value) switch
    {
        ("null", null) => true,
        ("object", JsonObject) => true,
        ("array", JsonArray) => true,
        ("string", JsonValue text) => text.TryGetValue(out string? _),
        ("boolean", JsonValue flag) => flag.TryGetValue(out bool _),
        ("integer", JsonValue number) => number.TryGetValue(out long _),
        ("number", JsonValue number) => number.TryGetValue(out double _),
        _ => false,
    };
}
