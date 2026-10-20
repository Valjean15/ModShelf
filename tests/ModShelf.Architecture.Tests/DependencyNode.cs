namespace ModShelf.Architecture.Tests;

/// <summary>
/// One project or package in the resolved dependency graph, with its direct dependencies.
/// </summary>
internal sealed record DependencyNode(string Name, string Kind, IReadOnlyList<string> Dependencies)
{
    public bool IsProject => string.Equals(Kind, "project", StringComparison.Ordinal);
}
