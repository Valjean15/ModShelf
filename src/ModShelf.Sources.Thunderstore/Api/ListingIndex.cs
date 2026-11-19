namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// What a community's <c>package-listing-index</c> answered: unchanged since asked, or the links of its listing's chunks.
/// </summary>
/// <param name="Unchanged">Whether the listing is as it was when last read, so the chunks weren't asked for.</param>
/// <param name="Chunks">The chunks' links, on Thunderstore's own hosts; empty when unchanged.</param>
/// <param name="LastModified">When Thunderstore built the listing, as it said.</param>
/// <param name="Blob">
/// The blob on the CDN the index pointed to, which lists the chunks' links; null when the index answered with them itself.
/// </param>
internal sealed record ListingIndex(bool Unchanged, IReadOnlyList<Uri> Chunks, DateTimeOffset? LastModified, Uri? Blob);
