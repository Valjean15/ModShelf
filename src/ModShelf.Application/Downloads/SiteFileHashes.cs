using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Downloads;

/// <summary>
/// The hashes a site gives for one of its files, from its list of the mod's files: what a file downloaded elsewhere,
/// such as by the browser, is checked against.
/// </summary>
internal static class SiteFileHashes
{
    /// <summary>
    /// The file's hashes as its site lists them; none when the site has no catalog, doesn't list the file, or can't be
    /// asked.
    /// </summary>
    public static async Task<IReadOnlyList<FileHash>> OfAsync(ISourceRegistry sources, ModFileRef file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(file);

        if (sources.Find<IModCatalog>(file.Mod.Game.Source) is not { } catalog)
        {
            return [];
        }

        try
        {
            IReadOnlyList<ModFile> files = await catalog.GetFilesAsync(file.Mod, cancellationToken).ConfigureAwait(false);
            return files.FirstOrDefault(listed => listed.Reference == file)?.Hashes ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or ArgumentException or InvalidOperationException or TimeoutException)
        {
            return [];
        }
    }

    /// <summary>
    /// The hash to check a file with: its SHA-256 when the site gives one, else another.
    /// </summary>
    public static FileHash? Best(IReadOnlyList<FileHash> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);

        return hashes.FirstOrDefault(hash => hash.Algorithm == FileHashAlgorithm.Sha256) ?? (hashes.Count > 0 ? hashes[0] : null);
    }
}
