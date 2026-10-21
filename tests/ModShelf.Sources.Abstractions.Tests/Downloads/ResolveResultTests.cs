using System.Reflection;

using ModShelf.Sources.Abstractions.Downloads;

namespace ModShelf.Sources.Abstractions.Tests.Downloads;

public sealed class ResolveResultTests
{
    [Fact]
    public void OnlyTicketsAndHandoffsExist()
    {
        // Callers switch over these cases. Adding one means updating every caller, so it should be a
        // deliberate change to this test too.
        string[] results =
        [
            .. typeof(ResolveResult).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(ResolveResult)))
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal),
        ];

        string[] expected = [nameof(DownloadTicket), nameof(HandoffRequired)];
        Assert.Equal(expected, results);
    }

    [Fact]
    public void OtherAssembliesCannotAddResults()
    {
        ConstructorInfo constructor = Assert.Single(
            typeof(ResolveResult).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic),
            candidate => candidate.GetParameters().Length == 0);

        Assert.True(constructor.IsFamilyAndAssembly, "ResolveResult's constructor must be private protected.");
    }
}
