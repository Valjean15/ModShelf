namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// How a run of the CLI ended, and what it wrote.
/// </summary>
internal sealed record CliResult(int ExitCode, string Output, string Errors)
{
    /// <summary>
    /// The row of <c>queue list</c> output that names <paramref name="fileName"/>.
    /// </summary>
    public string QueueRowFor(string fileName)
        => Assert.Single(
            Output.ReplaceLineEndings("\n").Split('\n'),
            line => line.Contains(fileName, StringComparison.Ordinal));

    public override string ToString() => $"Exit code {ExitCode}{Environment.NewLine}{Output}{Errors}";
}
