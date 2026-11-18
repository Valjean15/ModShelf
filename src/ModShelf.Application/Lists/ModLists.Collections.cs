using ModShelf.Sources.Abstractions.Collections;

namespace ModShelf.Application.Lists;

/// <summary>
/// A site's collection read as a list (ADR 0098), to import as a list file is: its files downloaded into the library,
/// never installed.
/// </summary>
internal sealed partial class ModLists
{
    public async Task<ModList?> ReadCollectionAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        foreach (IModCollections site in sources.All<IModCollections>())
        {
            if (await site.GetCollectionAsync(link, cancellationToken).ConfigureAwait(false) is { } collection)
            {
                LogCollectionRead(logger, collection.Name, collection.Game, collection.Files.Count, collection.Elsewhere.Count, collection.FilesUnavailable);
                return Listed(collection, timeProvider.GetUtcNow());
            }
        }

        return null;
    }

    /// <summary>
    /// The collection as a list: a mod for each mod its files are of, in the order it first lists them, to watch, with
    /// those files.
    /// </summary>
    internal static ModList Listed(CollectionRevision collection, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(collection);

        ListedMod[] mods =
        [
            .. collection.Files.GroupBy(file => file.File.Mod).Select(files => new ListedMod(
                files.Key,
                files.First().ModName,
                files.First().Author,
                null,
                Watched: true,
                Pinned: false,
                Note: null,
                [.. files.Select(file => new ListedFile(file.File, file.FileName, file.Version, file.Category, file.Optional))])),
        ];
        return new ModList(collection.Name, now, mods)
        {
            Elsewhere = [.. collection.Elsewhere.Select(resource => new ListedElsewhere(resource.Name, resource.PageUrl, resource.Optional))],
            FilesUnavailable = collection.FilesUnavailable,
        };
    }
}
