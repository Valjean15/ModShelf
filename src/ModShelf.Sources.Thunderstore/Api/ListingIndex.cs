namespace ModShelf.Sources.Thunderstore.Api;

/// <summary>
/// What a community's <c>package-listing-index</c> answered: unchanged since asked, or the links of its listing's chunks.
/// </summary>
/// <param name="Unchanged">Whether the listing is as it was when last read, so the chunks weren't asked for.</param>
/// <param name="Chunks">The chunks' links, on Thunderstore's own hosts; empty when unchanged.</param>
/// <param name="LastModified">When Thunderstore built the listing, as it said.</param>
internal sealed record ListingIndex(bool Unchanged, IReadOnlyList<Uri> Chunks, DateTimeOffset? LastModified);
