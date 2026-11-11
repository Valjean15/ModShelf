using System.Globalization;

using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Links;

namespace ModShelf.Sources.ModIo.Catalog;

/// <summary>
/// A mod's files as ModShelf lists them: its versions, one after the other. The live one is the main file, and each
/// earlier one an old version, replaced by the next, so following them leads to the live one, as Nexus Mods' update
/// chains do. A game with files per platform lists those approved for Windows. Each file's link is mod.io's link to
/// download it by.
/// </summary>
internal static class ModIoFiles
{
    /// <summary>
    /// What mod.io says of a file a platform approved.
    /// </summary>
    private const int Approved = 1;

    /// <param name="mod">The mod, with its live file for Windows.</param>
    /// <param name="files">Every file mod.io lists for it.</param>
    public static IReadOnlyList<ModFile> From(ModRef reference, ModIoMod mod, IEnumerable<ModIoModfile> files)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(files);

        ModIoModfile[] versions = Versions(mod, files);
        long? live = ModIoMods.LiveFile(mod);
        int liveAt = Array.FindIndex(versions, file => file.Id == live);
        List<ModFile> listed = [];
        for (int index = 0; index < versions.Length; index++)
        {
            long? replacedBy = index == liveAt ? null
                : liveAt >= 0 && index > liveAt ? versions[liveAt].Id
                : index + 1 < versions.Length ? versions[index + 1].Id
                : null;
            listed.Add(ToModFile(reference, versions[index], index == liveAt ? ModFileCategory.Main : ModFileCategory.OldVersion, replacedBy));
        }

        return listed;
    }

    /// <summary>
    /// What each version changed, as its author wrote it, the newest first; versions without a word of it left out.
    /// </summary>
    public static IReadOnlyList<ModChangelog> Changelogs(ModIoMod mod, IEnumerable<ModIoModfile> files)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(files);

        return
        [
            .. Versions(mod, files)
                .Reverse()
                .Where(file => !string.IsNullOrWhiteSpace(file.Changelog))
                .Select(file => new ModChangelog(
                    file.Version ?? string.Empty,
                    file.Changelog!.ReplaceLineEndings("\n").Split('\n'))),
        ];
    }

    /// <summary>
    /// The name a file is saved as: mod.io's, made to work on every system as the library's names are.
    /// </summary>
    public static string FileName(ModIoModfile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return string.IsNullOrWhiteSpace(file.Filename?.Replace('.', ' '))
            ? file.Id.ToString(CultureInfo.InvariantCulture)
            : LibraryNames.File(file.Filename.Trim());
    }

    /// <summary>
    /// The file's MD5 checksum, when mod.io gives one.
    /// </summary>
    public static FileHash? Hash(ModIoModfile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return file.Filehash?.Md5 is { Length: 32 } md5 && md5.All(char.IsAsciiHexDigit) ? new FileHash(FileHashAlgorithm.Md5, md5) : null;
    }

    /// <summary>
    /// The mod's files for Windows, each once, oldest first: those approved for it when the game has files per platform,
    /// and the live one; every file when it hasn't.
    /// </summary>
    private static ModIoModfile[] Versions(ModIoMod mod, IEnumerable<ModIoModfile> files)
    {
        bool perPlatform = mod.Platforms is { Count: > 0 };
        long? live = ModIoMods.LiveFile(mod);
        return
        [
            .. files
                .Where(file => file is { Id: > 0 })
                .Where(file => !perPlatform || file.Id == live || (file.Platforms ?? []).Any(platform =>
                    platform is { Status: Approved } && string.Equals(platform.Platform, ModIoMods.Platform, StringComparison.OrdinalIgnoreCase)))
                .DistinctBy(file => file.Id)
                .OrderBy(file => file.DateAdded)
                .ThenBy(file => file.Id),
        ];
    }

    private static ModFile ToModFile(ModRef mod, ModIoModfile file, ModFileCategory category, long? replacedBy)
    {
        string fileName = FileName(file);
        long gameId = ModIoLink.Id(mod.Game.Key);
        long modId = ModIoLink.Id(mod.Id);
        return new ModFile(
            new ModFileRef(mod, file.Id.ToString(CultureInfo.InvariantCulture)),
            fileName,
            fileName,
            file.Version,
            category,
            file.Filesize >= 0 ? file.Filesize : null,
            ModIoMods.Time(file.DateAdded) ?? DateTimeOffset.UnixEpoch,
            Hash(file) is { } hash ? [hash] : [],
            pageUrl: ModIoLink.FileLink(gameId, modId, file.Id),
            replacedBy: replacedBy is { } newer ? new ModFileRef(mod, newer.ToString(CultureInfo.InvariantCulture)) : null);
    }
}
