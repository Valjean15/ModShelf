namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// A version of a <see cref="ThunderstorePackage"/>, as a listing's chunk gives it.
/// </summary>
/// <param name="Icon">Its icon, 256 by 256.</param>
/// <param name="Dependencies">The packages it needs, as <c>Team-Name-1.2.3</c>.</param>
/// <param name="DateCreated">When it was uploaded.</param>
/// <param name="FileSize">Its zip's size in bytes.</param>
internal sealed record ThunderstoreVersion(
    string VersionNumber,
    string? Description,
    string? Icon,
    IReadOnlyList<string>? Dependencies,
    long Downloads,
    DateTimeOffset DateCreated,
    long FileSize);
