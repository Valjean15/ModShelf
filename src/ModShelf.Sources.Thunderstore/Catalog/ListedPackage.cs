namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// A package as its community's listing gives it, kept small: what ModShelf shows of it, and of its versions only what
/// it shows of each file. The description, the icon and the dependencies are its newest version's.
/// </summary>
/// <param name="Versions">Its active versions, the newest first, as Thunderstore orders them; never empty.</param>
internal sealed record ListedPackage(
    PackageName Name,
    IReadOnlyList<string> Categories,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Likes,
    bool IsDeprecated,
    bool HasAdultContent,
    string? Description,
    Uri? Icon,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<ListedVersion> Versions)
{
    public ListedVersion Newest => Versions[0];

    /// <summary>
    /// Every version's downloads, as Thunderstore's website counts the package's.
    /// </summary>
    public long Downloads => Versions.Sum(version => version.Downloads);

    /// <summary>
    /// The version numbered <paramref name="number"/>, or null when it isn't listed, such as one its author took down.
    /// </summary>
    public ListedVersion? Version(string number) => Versions.FirstOrDefault(version => version.Number == number);
}
