namespace ModShelf.Sources.Nexus.Api;

/// <summary>
/// What the <c>CollectionFiles</c> query answers: <c>data.collectionRevision</c>, a revision of a collection with its
/// files and what it needs from elsewhere.
/// </summary>
internal sealed record NexusCollectionFiles(NexusCollectionFiles.Revision? CollectionRevision)
{
    internal sealed record Revision(
        int? RevisionNumber,
        CollectionNode? Collection,
        IReadOnlyList<ModFileNode?>? ModFiles,
        IReadOnlyList<ResourceNode?>? ExternalResources);

    internal sealed record CollectionNode(string? Name, string? Slug);

    /// <summary>
    /// A file the revision lists, at the version it pins. <see cref="File"/> is null when Nexus Mods no longer offers it.
    /// </summary>
    internal sealed record ModFileNode(long FileId, bool? Optional, string? Version, FileNode? File);

    /// <param name="Category">The section of its mod's Files tab, such as <c>MAIN</c>.</param>
    internal sealed record FileNode(
        long FileId,
        long ModId,
        string? Name,
        string? Version,
        string? Category,
        NexusModNode.GameNode? Game,
        ModNode? Mod);

    internal sealed record ModNode(string? Name, string? Author);

    /// <summary>
    /// Something the collection needs from elsewhere: a page on another site, or a file bundled in the collection's own
    /// archive, which has no link.
    /// </summary>
    internal sealed record ResourceNode(string? Name, bool? Optional, string? ResourceType, string? ResourceUrl);
}
