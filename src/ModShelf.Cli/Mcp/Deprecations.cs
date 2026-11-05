namespace ModShelf.Cli.Mcp;

/// <summary>
/// Every argument the tools still take but will stop taking. An argument renamed in a release stays until the next major
/// version: a call that uses it works, with a <c>deprecated</c> block in its result, a note at the start of its message,
/// a warning in the log, and a count for the diagnostics report (<see cref="McpDeprecations"/>). Its description starts
/// with "Deprecated", and its input schema marks it <c>deprecated</c> (<see cref="DeprecatedAttribute"/>).
/// </summary>
internal static class Deprecations
{
    /// <summary>
    /// <c>download</c>'s one <c>link</c>, which <c>links</c>, a list, replaced in 0.9.3 (ADR 0061).
    /// </summary>
    public static Deprecation DownloadLink { get; } = new(
        "DL_LINK_ARG",
        "download",
        "link",
        "links",
        "2.0",
        "'link' is deprecated and stops working in ModShelf 2.0; pass 'links' as a list instead, as in the example.");

    public static IReadOnlyList<Deprecation> All { get; } = [DownloadLink];
}
