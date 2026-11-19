namespace ModShelf.Cli.Mcp;

/// <summary>
/// What became of the links given to download: one result each, in the order given.
/// </summary>
/// <param name="Requirements">
/// With <c>withRequirements</c>, what each link's mod needs and what became of it, in the order the links were given;
/// null otherwise.
/// </param>
/// <param name="Deprecated">
/// Set when the call used an argument <c>download</c> will stop taking, such as <c>link</c>: what to use instead. Null
/// otherwise.
/// </param>
/// <param name="Message">
/// How many are downloading, were in the library already, or couldn't be downloaded; after a note first when the call
/// used a deprecated argument.
/// </param>
internal sealed record DownloadsResult(
    IReadOnlyList<LinkDownloadResult> Results,
    IReadOnlyList<LinkRequirements>? Requirements,
    DeprecatedArgument<DownloadLinks>? Deprecated,
    string Message);
