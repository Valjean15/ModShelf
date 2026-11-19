namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// A version's <c>CHANGELOG.md</c>, from <c>api/experimental/package/{team}/{name}/{version}/changelog/</c>; null when it
/// has none.
/// </summary>
internal sealed record ThunderstoreMarkdown(string? Markdown);
