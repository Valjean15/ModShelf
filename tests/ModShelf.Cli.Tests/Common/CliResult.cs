namespace ModShelf.Cli.Tests.Common;

/// <summary>
/// How a run of the CLI ended, and what it wrote.
/// </summary>
internal sealed record CliResult(int ExitCode, string Output, string Errors)
{
    private string[] OutputLines => Output.ReplaceLineEndings("\n").Split('\n');

    /// <summary>
    /// The row of <c>queue list</c> output that names <paramref name="fileName"/>.
    /// </summary>
    public string QueueRowFor(string fileName)
        => Assert.Single(OutputLines, line => line.Contains(fileName, StringComparison.Ordinal));

    /// <summary>
    /// The row of <c>account list</c> output for <paramref name="source"/>, whose ID starts it.
    /// </summary>
    public string AccountRowFor(string source)
        => Assert.Single(OutputLines, line => line.StartsWith(source + " ", StringComparison.Ordinal));

    public override string ToString() => $"Exit code {ExitCode}{Environment.NewLine}{Output}{Errors}";
}
