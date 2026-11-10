using System.Globalization;
using System.Text.RegularExpressions;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Links;

namespace ModShelf.Sources.GameBanana.Catalog;

/// <summary>
/// A mod's files as GameBanana lists them, side by side: each archive a main file, a file it couldn't read, such as a
/// mod manager's release file, miscellaneous, and each older version its author keeps apart an old version, replaced by
/// the newer archive of the same name, its version left out (ADR 0087). Each file's link is its 1-click link, which the
/// app downloads it by.
/// </summary>
internal static partial class GameBananaFiles
{
    /// <summary>
    /// The mod's files, current and archived, each once.
    /// </summary>
    public static IReadOnlyList<ModFile> From(ModRef mod, GameBananaMod profile)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(profile);

        GameBananaFile[] listed = [.. (profile.Files ?? []).Where(file => file is not null)];
        GameBananaFile[] current = [.. listed.Where(file => !file.IsArchived)];
        GameBananaFile[] archived =
        [
            .. listed.Where(file => file.IsArchived).Concat((profile.ArchivedFiles ?? []).Where(file => file is not null))
                .DistinctBy(file => file.Id)
                .Where(file => current.All(kept => kept.Id != file.Id)),
        ];
        GameBananaFile[] archives = [.. current.Where(IsArchive)];

        return
        [
            .. current.Select(file => ToModFile(mod, file, IsArchive(file) ? ModFileCategory.Main : ModFileCategory.Miscellaneous, replacedBy: null)),
            .. archived.Select(file => ToModFile(mod, file, ModFileCategory.OldVersion, Successor(file, archives))),
        ];
    }

    /// <summary>
    /// What a link to the mod as a whole downloads: the archive uploaded last, or the file, when it has no archive.
    /// </summary>
    public static GameBananaFile? MainDownload(GameBananaMod profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        GameBananaFile[] current = [.. (profile.Files ?? []).Where(file => file is not null && !file.IsArchived)];
        return current.Where(IsArchive).OrderByDescending(file => file.DateAdded).ThenByDescending(file => file.Id).FirstOrDefault()
            ?? current.OrderByDescending(file => file.DateAdded).ThenByDescending(file => file.Id).FirstOrDefault();
    }

    /// <summary>
    /// One of the mod's files, current or archived.
    /// </summary>
    public static GameBananaFile? Find(GameBananaMod profile, string fileId)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return (profile.Files ?? []).Concat(profile.ArchivedFiles ?? [])
            .FirstOrDefault(file => file is not null && file.Id.ToString(CultureInfo.InvariantCulture) == fileId);
    }

    /// <summary>
    /// A file's name with its version left out, to tell which newer file an archived one is an older version of: its
    /// letters alone, lowercase, without its extension or the five hexadecimal digits GameBanana adds to a name it has
    /// had already. <c>p5r_female_protagonist1_2_27.7z</c> is <c>prfemaleprotagonist</c>.
    /// </summary>
    public static string Stem(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        string name = Path.GetFileNameWithoutExtension(fileName.Trim()).ToLowerInvariant();
        name = AddedDigits().Replace(name, string.Empty);
        return string.Concat(name.Where(char.IsLetter));
    }

    /// <summary>
    /// The name a file is saved as: GameBanana's, made to work on every system as the library's names are.
    /// </summary>
    public static string FileName(GameBananaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return string.IsNullOrWhiteSpace(file.FileName?.Replace('.', ' '))
            ? file.Id.ToString(CultureInfo.InvariantCulture)
            : LibraryNames.File(file.FileName.Trim());
    }

    /// <summary>
    /// The file's MD5 checksum, when GameBanana gives one.
    /// </summary>
    public static FileHash? Hash(GameBananaFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.Md5 is { Length: 32 } md5 && md5.All(char.IsAsciiHexDigit) ? new FileHash(FileHashAlgorithm.Md5, md5) : null;
    }

    /// <summary>
    /// Whether GameBanana read what's in the file, as it does an archive; one it doesn't say of is taken for one.
    /// </summary>
    private static bool IsArchive(GameBananaFile file) => file.HasContents != false;

    /// <summary>
    /// The newest archive uploaded after an archived file whose name is the same, its version left out; null when none.
    /// </summary>
    private static string? Successor(GameBananaFile file, GameBananaFile[] archives)
    {
        string stem = Stem(file.FileName ?? string.Empty);
        return stem.Length == 0
            ? null
            : archives
                .Where(archive => archive.DateAdded > file.DateAdded && Stem(archive.FileName ?? string.Empty) == stem)
                .OrderByDescending(archive => archive.DateAdded)
                .Select(archive => archive.Id.ToString(CultureInfo.InvariantCulture))
                .FirstOrDefault();
    }

    private static ModFile ToModFile(ModRef mod, GameBananaFile file, ModFileCategory category, string? replacedBy)
    {
        string fileName = FileName(file);
        return new ModFile(
            new ModFileRef(mod, file.Id.ToString(CultureInfo.InvariantCulture)),
            string.IsNullOrWhiteSpace(file.Description) ? fileName : file.Description.Trim(),
            fileName,
            file.Version,
            category,
            file.Size is >= 0 ? file.Size : null,
            GameBananaMods.Time(file.DateAdded) ?? DateTimeOffset.UnixEpoch,
            Hash(file) is { } hash ? [hash] : [],
            pageUrl: GameBananaLink.FileLink(mod.Id, file.Id.ToString(CultureInfo.InvariantCulture)),
            replacedBy: replacedBy is null ? null : new ModFileRef(mod, replacedBy));
    }

    /// <summary>
    /// The five hexadecimal digits, after an underscore, GameBanana adds to the end of a name it has had already.
    /// </summary>
    [GeneratedRegex("_[0-9a-f]{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex AddedDigits();
}
