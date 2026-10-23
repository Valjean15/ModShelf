using System.Reflection;

namespace ModShelf.Architecture.Tests;

/// <summary>
/// One test per dependency rule in CLAUDE.md. Each rule is checked twice: against the declared
/// references (project and package), and against the assembly references the compiler emitted.
/// </summary>
public sealed class DependencyRuleTests
{
    private static readonly ProjectGraph Graph = ProjectGraph.Load();

    private static readonly string[] Hosts = [ProjectNames.Desktop, ProjectNames.Cli];

    private static readonly string[] ExpectedProjects =
    [
        ProjectNames.Domain,
        ProjectNames.SourcesAbstractions,
        ProjectNames.Application,
        ProjectNames.Infrastructure,
        ProjectNames.Platform,
        ProjectNames.Presentation,
        ProjectNames.SourcesNexus,
        ProjectNames.SourcesDirectUrl,
        ProjectNames.Cli,
    ];

    [Fact]
    public void ArchitectureTestsSeeEveryProject()
    {
        string[] missing = [.. ExpectedProjects.Except(Graph.Projects.Select(project => project.Name))];

        AssertNoViolations("The architecture tests must reference every project under src except Desktop", missing);
    }

    [Fact]
    public void DomainReferencesNothing()
    {
        DependencyNode domain = Graph.GetProject(ProjectNames.Domain);

        List<string> violations = FindViolations(ProjectNames.Domain, allowed: []);
        violations.AddRange(domain.Dependencies
            .Where(dependency => !IsModShelf(dependency))
            .Select(dependency => $"it references the package {dependency}"));

        AssertNoViolations($"{ProjectNames.Domain} may not reference any project or package", violations);
    }

    [Fact]
    public void SourcesAbstractionsReferencesOnlyDomain()
    {
        AssertReferencesOnly(ProjectNames.SourcesAbstractions, ProjectNames.Domain);
    }

    [Fact]
    public void ApplicationReferencesOnlyDomainAndSourcesAbstractions()
    {
        AssertReferencesOnly(ProjectNames.Application, ProjectNames.Domain, ProjectNames.SourcesAbstractions);
    }

    [Fact]
    public void SourcesReferenceOnlySourcesAbstractions()
    {
        DependencyNode[] sources =
        [
            .. Graph.Projects.Where(project =>
                project.Name.StartsWith(ProjectNames.SourcePrefix, StringComparison.Ordinal)
                && !string.Equals(project.Name, ProjectNames.SourcesAbstractions, StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(sources);
        foreach (DependencyNode source in sources)
        {
            AssertReferencesOnly(source.Name, ProjectNames.SourcesAbstractions);
        }
    }

    [Theory]
    [InlineData(ProjectNames.Infrastructure)]
    [InlineData(ProjectNames.Platform)]
    [InlineData(ProjectNames.Presentation)]
    public void InfrastructurePlatformAndPresentationReferenceOnlyApplication(string project)
    {
        AssertReferencesOnly(project, ProjectNames.Application);
    }

    [Fact]
    public void NoProjectReferencesAHost()
    {
        List<string> violations = [];
        foreach (DependencyNode project in Graph.Projects.Where(project => !Hosts.Contains(project.Name)))
        {
            violations.AddRange(project.Dependencies
                .Where(name => Hosts.Contains(name))
                .Select(host => $"{project.Name} references {host}"));
            violations.AddRange(CompiledReferencesOf(project.Name)
                .Where(name => Hosts.Contains(name))
                .Select(host => $"{project.Name}'s compiled code uses {host}"));
        }

        AssertNoViolations("Only the hosts are composition roots, so no project may reference them", violations);
    }

    [Fact]
    public void OnlyDesktopReferencesAvalonia()
    {
        List<string> violations = [];
        foreach (DependencyNode project in Graph.Projects.Where(project => project.Name != ProjectNames.Desktop))
        {
            violations.AddRange(Graph.TransitiveDependenciesOf(project.Name)
                .Where(IsAvalonia)
                .Select(dependency => $"{project.Name} depends on {dependency}"));
            violations.AddRange(CompiledReferencesOf(project.Name)
                .Where(IsAvalonia)
                .Select(assembly => $"{project.Name}'s compiled code uses {assembly}"));
        }

        AssertNoViolations($"Only {ProjectNames.Desktop} may reference Avalonia", violations);
    }

    private static void AssertReferencesOnly(string project, params string[] allowed)
    {
        AssertNoViolations(
            $"{project} may reference only {string.Join(" and ", allowed)}",
            FindViolations(project, allowed));
    }

    /// <summary>
    /// ModShelf projects that <paramref name="project"/> references directly, or whose types its
    /// compiled code uses, beyond <paramref name="allowed"/> and what those bring in transitively.
    /// </summary>
    private static List<string> FindViolations(string project, string[] allowed)
    {
        IReadOnlySet<string> reachable = Graph.Including(allowed);

        List<string> violations =
        [
            .. Graph.GetProject(project).Dependencies
                .Where(dependency => IsModShelf(dependency) && !allowed.Contains(dependency))
                .Select(dependency => $"it references {dependency}"),
            .. CompiledReferencesOf(project)
                .Where(assembly => IsModShelf(assembly) && !reachable.Contains(assembly))
                .Select(assembly => $"its compiled code uses {assembly}"),
        ];

        return violations;
    }

    private static IEnumerable<string> CompiledReferencesOf(string project)
        => Assembly.Load(new AssemblyName(project))
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);

    private static bool IsModShelf(string name)
        => name.StartsWith(ProjectNames.Prefix, StringComparison.Ordinal);

    private static bool IsAvalonia(string name)
        => name.StartsWith(ProjectNames.AvaloniaPrefix, StringComparison.OrdinalIgnoreCase);

    private static void AssertNoViolations(string rule, IReadOnlyCollection<string> violations)
    {
        if (violations.Count > 0)
        {
            Assert.Fail($"{rule}, but:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", violations)}");
        }
    }
}
