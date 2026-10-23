using System.Reflection;

namespace ModShelf.Architecture.Tests;

/// <summary>
/// The visibility rule in docs/code-organization.md: projects that implement things show other projects only
/// what they may use. Behaviour is shared through interfaces and data through records; the classes behind them
/// stay internal, so no other project can depend on an implementation.
/// </summary>
public sealed class PublicSurfaceTests
{
    private static readonly ProjectGraph Graph = ProjectGraph.Load();

    [Fact]
    public void ImplementingProjectsMakeOnlyTheirContractsPublic()
    {
        string[] projects = [.. ImplementingProjects()];
        Assert.Contains(ProjectNames.Application, projects);
        Assert.Contains(ProjectNames.SourcesDirectUrl, projects);

        string[] violations =
        [
            .. projects.SelectMany(project => Assembly.Load(new AssemblyName(project))
                .GetExportedTypes()
                .Where(type => !MayBePublic(type, project))
                .Select(type => $"{type.FullName} in {project}")),
        ];

        Violations.AssertNone(
            "Only interfaces, enums, records, exceptions, Add…() classes, a source's main class and EF migrations may be public; make these internal",
            violations);
    }

    /// <summary>
    /// Application, Infrastructure, Platform and every source. Domain and Sources.Abstractions are all contract,
    /// Presentation's ViewModels are bound by Desktop's views, and the hosts are executables.
    /// </summary>
    private static IEnumerable<string> ImplementingProjects()
        => Graph.Projects
            .Select(project => project.Name)
            .Where(name => name is ProjectNames.Application or ProjectNames.Infrastructure or ProjectNames.Platform
                || (name.StartsWith(ProjectNames.SourcePrefix, StringComparison.Ordinal)
                    && name != ProjectNames.SourcesAbstractions));

    private static bool MayBePublic(Type type, string project)
        => type.IsInterface
            || type.IsEnum
            || IsRecord(type)
            || typeof(Exception).IsAssignableFrom(type)
            || IsServiceRegistration(type)
            || IsMigration(type)
            || (project.StartsWith(ProjectNames.SourcePrefix, StringComparison.Ordinal) && IsSourceClass(type));

    // The compiler gives every record class a public "<Clone>$" method, and every record struct a PrintMembers method.
    private static bool IsRecord(Type type)
        => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null
            || (type.IsValueType && type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null);

    // Static classes are abstract and sealed in IL.
    private static bool IsServiceRegistration(Type type)
        => type is { IsAbstract: true, IsSealed: true }
            && type.Name.EndsWith("ServiceCollectionExtensions", StringComparison.Ordinal);

    // dotnet ef generates migrations as public classes; they're never edited by hand.
    private static bool IsMigration(Type type)
        => type.BaseType?.FullName == "Microsoft.EntityFrameworkCore.Migrations.Migration";

    // A source's main class stays public: the shared SourceContractTests are typed by it.
    private static bool IsSourceClass(Type type)
        => type is { IsClass: true, IsAbstract: false }
            && type.GetInterfaces().Any(contract => contract.FullName == $"{ProjectNames.SourcesAbstractions}.IModSource");
}
