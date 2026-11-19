namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// A community's packages, as its listing gave them when it was read, in the listing's order: pinned first, then
/// deprecated last, then by when each was updated, the latest first.
/// </summary>
internal sealed class Listing
{
    private readonly Dictionary<string, ListedPackage> _byName;

    /// <param name="lastModified">When Thunderstore built the listing, as its index said; null when it didn't say.</param>
    /// <param name="blob">The blob its index pointed to; null when the index listed the chunks itself.</param>
    public Listing(string community, IReadOnlyList<ListedPackage> packages, DateTimeOffset readAt, DateTimeOffset? lastModified, Uri? blob = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(community);
        ArgumentNullException.ThrowIfNull(packages);

        Community = community;
        Packages = packages;
        ReadAt = readAt;
        LastModified = lastModified;
        Blob = blob;

        // Thunderstore takes a team's name in any case, and so do links to its packages.
        _byName = new Dictionary<string, ListedPackage>(packages.Count, StringComparer.OrdinalIgnoreCase);
        foreach (ListedPackage package in packages)
        {
            _byName.TryAdd(package.Name.FullName, package);
        }
    }

    private Listing(Listing read, DateTimeOffset readAt)
    {
        Community = read.Community;
        Packages = read.Packages;
        ReadAt = readAt;
        LastModified = read.LastModified;
        Blob = read.Blob;
        _byName = read._byName;
    }

    public string Community { get; }

    public IReadOnlyList<ListedPackage> Packages { get; }

    /// <summary>
    /// When it was read, or found unchanged, by the source's clock.
    /// </summary>
    public DateTimeOffset ReadAt { get; }

    public DateTimeOffset? LastModified { get; }

    /// <summary>
    /// The blob on the CDN its index pointed to, whose link names its SHA-256, so an index pointing there again is
    /// unchanged; null when the index listed the chunks itself.
    /// </summary>
    public Uri? Blob { get; }

    /// <summary>
    /// The package named <paramref name="fullName"/>, <c>Team-Name</c>, in any case; null when the community doesn't
    /// list it.
    /// </summary>
    public ListedPackage? Find(string fullName) => _byName.GetValueOrDefault(fullName);

    /// <summary>
    /// The same packages, read again or found unchanged at <paramref name="readAt"/>.
    /// </summary>
    public Listing ReadAgainAt(DateTimeOffset readAt) => new(this, readAt);
}
