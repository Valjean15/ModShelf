namespace ModShelf.Cli.Mcp;

/// <summary>
/// What one link's mod needs, as its site lists it, for <c>download</c> with <c>withRequirements</c>.
/// </summary>
/// <param name="Link">The link, as given.</param>
/// <param name="Listed">Whether its site lists what mods need; false too when it couldn't be read.</param>
/// <param name="Requirements">What the mod needs, each followed by what it needs in turn.</param>
/// <param name="Problem">Why what it needs couldn't be read; null when it was.</param>
/// <param name="Message">What it needs, in a sentence or two.</param>
internal sealed record LinkRequirements(
    string Link,
    bool Listed,
    IReadOnlyList<RequirementEntry> Requirements,
    string? Problem,
    string Message);
