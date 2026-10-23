namespace ModShelf.Architecture.Tests;

/// <summary>
/// Fails a rule with every violation listed, so one run shows all of them.
/// </summary>
internal static class Violations
{
    public static void AssertNone(string rule, IReadOnlyCollection<string> violations)
    {
        if (violations.Count > 0)
        {
            Assert.Fail($"{rule}, but:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", violations)}");
        }
    }
}
