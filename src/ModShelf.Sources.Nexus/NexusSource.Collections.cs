using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Collections;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IModCollections"/> half: the <c>CollectionFiles</c> query against the v2 GraphQL API, one request for
/// a collection's revision with every file it lists. ModShelf downloads those files into the library and never runs a
/// collection's install steps.
/// </summary>
public sealed partial class NexusSource : IModCollections
{
    private static readonly string CollectionFilesQuery = ReadQuery("Collections", "CollectionFiles.graphql");

    /// <returns>
    /// The revision the link names, or the latest published: its files in the order Nexus Mods lists them, each once,
    /// and what it needs from elsewhere. Null when the link isn't to a collection on Nexus Mods.
    /// </returns>
    /// <exception cref="ArgumentException">The link is to a collection, but its game, slug or revision can't be read.</exception>
    /// <exception cref="HttpRequestException">
    /// The API couldn't be reached, or refused the query, as it does for a collection it doesn't have.
    /// </exception>
    public async Task<CollectionRevision?> GetCollectionAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (NexusCollectionLink.Read(link) is not { } collection)
        {
            return null;
        }

        JsonObject variables = new()
        {
            ["slug"] = collection.Slug,
            ["domainName"] = collection.Game,
            ["revision"] = collection.Revision,
        };
        NexusCollectionFiles found = await _api.QueryAsync<NexusCollectionFiles>(CollectionFilesQuery, variables, cancellationToken).ConfigureAwait(false);
        NexusCollectionFiles.Revision revision = found.CollectionRevision
            ?? throw new HttpRequestException("Nexus Mods has no such collection.", null, HttpStatusCode.NotFound);

        List<CollectionFile> files = [];
        HashSet<ModFileRef> listed = [];
        int unavailable = 0;
        foreach (NexusCollectionFiles.ModFileNode? entry in revision.ModFiles ?? [])
        {
            if (entry is null)
            {
                continue;
            }

            if (ToCollectionFile(entry, collection.Game) is not { } file)
            {
                unavailable++;
            }
            else if (listed.Add(file.File))
            {
                files.Add(file);
            }
        }

        CollectionResource[] elsewhere = [.. (revision.ExternalResources ?? []).OfType<NexusCollectionFiles.ResourceNode>().Select(ToResource).OfType<CollectionResource>()];
        return new CollectionRevision(
            Named(revision.Collection?.Name) ?? collection.Slug,
            new GameRef(NexusId, collection.Game),
            collection.PageUrl,
            revision.RevisionNumber ?? collection.Revision,
            files,
            elsewhere,
            unavailable);
    }

    /// <returns>The file; null when Nexus Mods no longer offers it, or names it with IDs it doesn't give.</returns>
    private static CollectionFile? ToCollectionFile(NexusCollectionFiles.ModFileNode entry, string collectionGame)
    {
        if (entry.File is not { } file
            || NexusLink.FromApi(file.Game?.DomainName ?? collectionGame, file.ModId) is not { } mod
            || file.FileId <= 0)
        {
            return null;
        }

        string fileId = file.FileId.ToString(CultureInfo.InvariantCulture);
        return new CollectionFile(
            new ModFileRef(new ModRef(new GameRef(NexusId, mod.Game), mod.ModId), fileId),
            Named(file.Mod?.Name) ?? $"Mod {mod.ModId}",
            Named(file.Name) ?? $"File {fileId}",
            Named(entry.Version) ?? file.Version,
            CategoryNamed(file.Category),
            entry.Optional == true,
            file.Mod?.Author);
    }

    /// <returns>What's needed, with its page when that's a web link; null for one with no name.</returns>
    private static CollectionResource? ToResource(NexusCollectionFiles.ResourceNode resource)
        => Named(resource.Name) is { } name ? new CollectionResource(name, WebLink(resource.ResourceUrl), resource.Optional == true) : null;
}
