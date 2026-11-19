using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.CurseForge.Api;

namespace ModShelf.Sources.CurseForge.Catalog;

/// <summary>
/// A mod's files on CurseForge as ModShelf's: each build is for the game versions and loaders it lists, beside the
/// builds for others, so a file's update is the newest later build of the same kind (release, beta or alpha) for the
/// same game versions, never one for others, nor one withdrawn (ADR 0107).
/// </summary>
internal static class CurseForgeFiles
{
    private const int Release = 1;
    private const int Archived = 8;
    private const int Deprecated = 12;
    private const int Sha1 = 1;
    private const int Md5 = 2;

    /// <summary>
    /// The files, each linked to its update, newest first.
    /// </summary>
    public static IReadOnlyList<ModFile> Of(ModRef mod, Uri modPage, IEnumerable<CurseForgeFile> files)
    {
        CurseForgeFile[] newestFirst = [.. files.OrderByDescending(file => file.FileDate).ThenByDescending(file => file.Id)];
        Dictionary<string, CurseForgeFile> newerOfKind = new(StringComparer.Ordinal);
        List<ModFile> listed = [];
        foreach (CurseForgeFile file in newestFirst)
        {
            string kind = KindOf(file);
            ModFileRef? replacedBy = newerOfKind.TryGetValue(kind, out CurseForgeFile? newer) ? Reference(mod, newer) : null;
            ModFile mapped = Of(mod, modPage, file, replacedBy);
            listed.Add(mapped);

            // A file withdrawn is no one's update.
            if (mapped.Category != ModFileCategory.Archived)
            {
                newerOfKind[kind] = file;
            }
        }

        return listed;
    }

    public static ModFile Of(ModRef mod, Uri modPage, CurseForgeFile file, ModFileRef? replacedBy = null)
    {
        string fileName = (string.IsNullOrWhiteSpace(file.FileName) ? Number(file.Id) : file.FileName.Trim()).Replace('/', '_').Replace('\\', '_');
        string name = string.IsNullOrWhiteSpace(file.DisplayName) ? fileName : file.DisplayName.Trim();
        return new ModFile(
            Reference(mod, file),
            name,
            fileName,
            versionLabel: null,
            CategoryOf(file),
            file.FileLength >= 0 ? file.FileLength : null,
            file.FileDate,
            Hashes(file),
            PageOf(modPage, file.Id),
            replacedBy);
    }

    public static ModFileRef Reference(ModRef mod, CurseForgeFile file) => new(mod, Number(file.Id));

    /// <summary>
    /// Each hash CurseForge gives that's what it says: SHA-1 and MD5.
    /// </summary>
    public static IReadOnlyList<FileHash> Hashes(CurseForgeFile file)
        => [.. (file.Hashes ?? []).Select(Hash).OfType<FileHash>().DistinctBy(hash => hash.Algorithm)];

    /// <summary>
    /// The file's page, such as <c>…/minecraft/mc-mods/modmenu/files/1234</c>.
    /// </summary>
    public static Uri PageOf(Uri modPage, long file) => new($"{modPage.AbsoluteUri.TrimEnd('/')}/files/{Number(file)}");

    /// <summary>
    /// The page that downloads the file in the browser, for a mod whose author lets no app download it.
    /// </summary>
    public static Uri DownloadPageOf(Uri modPage, long file) => new($"{modPage.AbsoluteUri.TrimEnd('/')}/download/{Number(file)}");

    /// <summary>
    /// Withdrawn files are archived; a release is a main file, and a beta or an alpha optional.
    /// </summary>
    private static ModFileCategory CategoryOf(CurseForgeFile file)
        => !file.IsAvailable || file.FileStatus is Archived or Deprecated ? ModFileCategory.Archived
            : file.ReleaseType == Release ? ModFileCategory.Main
            : ModFileCategory.Optional;

    /// <summary>
    /// What a file is a build of: its kind and the game versions and loaders it's for, in any order.
    /// </summary>
    private static string KindOf(CurseForgeFile file)
        => $"{Number(file.ReleaseType)}|{string.Join('|', (file.GameVersions ?? []).Select(version => version.Trim().ToUpperInvariant()).Order(StringComparer.Ordinal))}";

    private static FileHash? Hash(CurseForgeHash hash)
    {
        FileHashAlgorithm? algorithm = hash.Algo switch
        {
            Sha1 => FileHashAlgorithm.Sha1,
            Md5 => FileHashAlgorithm.Md5,
            _ => null,
        };
        int length = algorithm == FileHashAlgorithm.Sha1 ? 40 : 32;
        string value = hash.Value?.Trim() ?? string.Empty;
        return algorithm is { } known && value.Length == length && value.All(char.IsAsciiHexDigit) ? new FileHash(known, value) : null;
    }

    private static string Number(long number) => number.ToString(CultureInfo.InvariantCulture);
}
