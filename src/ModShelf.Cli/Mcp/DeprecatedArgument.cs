namespace ModShelf.Cli.Mcp;

/// <summary>
/// An argument a call used that a tool still takes, but will stop taking: what to use instead, with the call as it
/// should be made. Scripts can look for <paramref name="Code"/>; the result's message says the same for the agent.
/// </summary>
/// <param name="Code">The deprecation's code, also in the log's warning, such as <c>DL_LINK_ARG</c>.</param>
/// <param name="Argument">The old argument, such as <c>link</c>.</param>
/// <param name="Use">The argument to use instead, such as <c>links</c>.</param>
/// <param name="Example">The call's arguments as they should be given, with the caller's own values.</param>
internal sealed record DeprecatedArgument(string Code, string Argument, string Use, DownloadLinks Example);
