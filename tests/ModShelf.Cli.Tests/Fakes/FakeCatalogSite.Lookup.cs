using System.Collections.Concurrent;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Lookup;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// Finds the files of its game by MD5, as the test says each MD5 is: at first, none.
/// </summary>
internal sealed partial class FakeCatalogSite : IFileLookup
{
    public FileHashAlgorithm LookupAlgorithm => FileHashAlgorithm.Md5;

    /// <summary>
    /// The file each MD5 is, by its mod's and its own ID.
    /// </summary>
    public ConcurrentDictionary<string, (string Mod, string File)> ByMd5 { get; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<FoundFile>> FindByHashAsync(GameRef game, FileHash hash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(hash);
        if (game.Source != SourceId || hash.Algorithm != FileHashAlgorithm.Md5)
        {
            throw new ArgumentException("That's another site's game, or not an MD5.", nameof(game));
        }

        if (!ByMd5.TryGetValue(hash.Value, out (string Mod, string File) found))
        {
            return [];
        }

        ModRef mod = new(game, found.Mod);
        ModInfo info = await GetModAsync(mod, cancellationToken).ConfigureAwait(false);
        ModFile file = (await GetFilesAsync(mod, cancellationToken).ConfigureAwait(false)).Single(listed => listed.Reference.Id == found.File);
        return [new FoundFile(info, file)];
    }
}
