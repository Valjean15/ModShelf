namespace ModShelf.Application.Diagnostics;

/// <summary>
/// A deprecated argument callers still use, as counted between runs: how many calls used it, and when the first and the
/// last were made. The diagnostics report says it, so it's known when the argument can go.
/// </summary>
/// <param name="Name">Which, as its tool and argument, such as <c>download.link</c>.</param>
/// <param name="Code">Its code, such as <c>DL_LINK_ARG</c>.</param>
/// <param name="Count">How many calls used it, 1 or more.</param>
/// <param name="First">When the first counted call was made.</param>
/// <param name="Last">When the last was made.</param>
/// <param name="Client">What made the last, such as an MCP client's name; null when it didn't say.</param>
public sealed record DeprecatedCall(string Name, string Code, long Count, DateTimeOffset First, DateTimeOffset Last, string? Client);
