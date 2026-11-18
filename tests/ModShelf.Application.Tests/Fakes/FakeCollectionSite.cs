using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Collections;
using ModShelf.Sources.Abstractions.Common;

namespace ModShelf.Application.Tests.Fakes;

/// <summary>
/// A site with collections, as the test sets them, by link; any other link isn't one of its collections.
/// </summary>
internal sealed class FakeCollectionSite : IModCollections
{
    public static SourceId SourceId { get; } = new("fake-collections");

    public static GameRef Game { get; } = new(SourceId, "skyrimspecialedition");

    public SourceId Id => SourceId;

    public string DisplayName => "Fake collection site";

    public SiteWords Words => SiteWords.None;

    /// <summary>
    /// The collections, by their links.
    /// </summary>
    public Dictionary<Uri, CollectionRevision> Collections { get; } = [];

    /// <summary>
    /// What reading any collection fails with, when set.
    /// </summary>
    public Exception? Refusal { get; set; }

    /// <summary>
    /// Each link it was asked about.
    /// </summary>
    public List<Uri> Asked { get; } = [];

    public Task<CollectionRevision?> GetCollectionAsync(Uri link, CancellationToken cancellationToken)
    {
        Asked.Add(link);
        if (!Collections.TryGetValue(link, out CollectionRevision? collection))
        {
            return Task.FromResult<CollectionRevision?>(null);
        }

        return Refusal is { } refusal ? Task.FromException<CollectionRevision?>(refusal) : Task.FromResult<CollectionRevision?>(collection);
    }
}
