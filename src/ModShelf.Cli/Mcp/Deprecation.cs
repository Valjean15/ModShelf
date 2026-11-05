namespace ModShelf.Cli.Mcp;

/// <summary>
/// An argument a tool still takes until <paramref name="RemovedIn"/>, the next major version, since another replaced it.
/// Each is listed in <see cref="Deprecations"/>, and its parameter is marked <see cref="DeprecatedAttribute"/>.
/// </summary>
/// <param name="Code">Its code, in the result's <c>deprecated</c> block, the log's warning and the count, such as
/// <c>DL_LINK_ARG</c>.</param>
/// <param name="Tool">The tool, such as <c>download</c>.</param>
/// <param name="Argument">The old argument, such as <c>link</c>.</param>
/// <param name="Use">The argument to use instead, such as <c>links</c>.</param>
/// <param name="RemovedIn">The version that stops taking it, such as <c>2.0</c>.</param>
/// <param name="Note">What the result's message starts with, after "Note: ", for the agent.</param>
internal sealed record Deprecation(string Code, string Tool, string Argument, string Use, string RemovedIn, string Note)
{
    /// <summary>
    /// Its name in the count and the diagnostics report: the tool and the argument, such as <c>download.link</c>.
    /// </summary>
    public string Name => $"{Tool}.{Argument}";
}
