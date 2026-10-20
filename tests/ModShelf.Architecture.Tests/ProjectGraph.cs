using System.Reflection;
using System.Text.Json;

namespace ModShelf.Architecture.Tests;

/// <summary>
/// The resolved dependency graph of the ModShelf projects, read from this test assembly's
/// <c>.deps.json</c>. It lists every project and package reference after MSBuild has evaluated
/// the project files, including references that no code uses yet. A type-level scan (NetArchTest,
/// ArchUnitNET) cannot see those, because the compiler drops unused assembly references.
/// </summary>
internal sealed class ProjectGraph
{
    private readonly Dictionary<string, DependencyNode> _nodes;
    private readonly string _rootName;

    private ProjectGraph(Dictionary<string, DependencyNode> nodes, string rootName)
    {
        _nodes = nodes;
        _rootName = rootName;
    }

    /// <summary>
    /// Every ModShelf project in the graph, except this test project itself.
    /// </summary>
    public IEnumerable<DependencyNode> Projects => _nodes.Values
        .Where(node => node.IsProject
            && node.Name.StartsWith(ProjectNames.Prefix, StringComparison.Ordinal)
            && !string.Equals(node.Name, _rootName, StringComparison.Ordinal))
        .OrderBy(node => node.Name, StringComparer.Ordinal);

    public static ProjectGraph Load()
    {
        string rootName = typeof(ProjectGraph).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The test assembly has no name.");
        string path = Path.Combine(AppContext.BaseDirectory, rootName + ".deps.json");

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement json = document.RootElement;

        string targetName = json.GetProperty("runtimeTarget").GetProperty("name").GetString()
            ?? throw new InvalidOperationException($"{path} has no runtime target.");
        JsonElement target = json.GetProperty("targets").GetProperty(targetName);
        JsonElement libraries = json.GetProperty("libraries");

        Dictionary<string, DependencyNode> nodes = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in target.EnumerateObject())
        {
            // Keys look like "ModShelf.Domain/1.0.0".
            string name = entry.Name.Split('/')[0];
            string kind = libraries.TryGetProperty(entry.Name, out JsonElement library)
                && library.TryGetProperty("type", out JsonElement type)
                    ? type.GetString() ?? string.Empty
                    : string.Empty;
            string[] dependencies = entry.Value.TryGetProperty("dependencies", out JsonElement declared)
                ? [.. declared.EnumerateObject().Select(dependency => dependency.Name)]
                : [];

            nodes[name] = new DependencyNode(name, kind, dependencies);
        }

        return new ProjectGraph(nodes, rootName);
    }

    public DependencyNode GetProject(string name)
    {
        if (_nodes.TryGetValue(name, out DependencyNode? node) && node.IsProject)
        {
            return node;
        }

        throw new InvalidOperationException(
            $"{name} is not in the dependency graph of {_rootName}. Check its ProjectReference items.");
    }

    /// <summary>
    /// Everything <paramref name="name"/> depends on, directly or transitively.
    /// </summary>
    public IReadOnlySet<string> TransitiveDependenciesOf(string name)
    {
        HashSet<string> visited = new(StringComparer.Ordinal);
        Queue<string> pending = new(GetProject(name).Dependencies);

        while (pending.TryDequeue(out string? current))
        {
            if (visited.Add(current) && _nodes.TryGetValue(current, out DependencyNode? node))
            {
                foreach (string dependency in node.Dependencies)
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        return visited;
    }

    /// <summary>
    /// The given projects plus everything they depend on, directly or transitively.
    /// </summary>
    public IReadOnlySet<string> Including(IEnumerable<string> names)
    {
        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            result.Add(name);
            result.UnionWith(TransitiveDependenciesOf(name));
        }

        return result;
    }
}
