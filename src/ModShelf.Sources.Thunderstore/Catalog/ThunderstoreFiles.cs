using ModShelf.Domain.Mods;
using ModShelf.Sources.Thunderstore.Links;

namespace ModShelf.Sources.Thunderstore.Catalog;

/// <summary>
/// A package's versions as files: the newest the main one, each older one an old version, replaced by the next newer one
/// when that one was uploaded after it. Each is named as Thunderstore names its zips, <c>Team-Name-1.2.3.zip</c>, with
/// its page as its link. Thunderstore gives no hashes.
/// </summary>
internal static class ThunderstoreFiles
{
    public static IReadOnlyList<ModFile> Of(ModRef mod, ListedPackage package)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(package);

        List<ModFile> files = [];
        for (int index = 0; index < package.Versions.Count; index++)
        {
            ListedVersion version = package.Versions[index];
            ListedVersion? newer = index > 0 ? package.Versions[index - 1] : null;
            files.Add(new ModFile(
                new ModFileRef(mod, version.Number),
                $"{package.Name.DisplayName} {version.Number}",
                FileName(package.Name, version.Number),
                version.Number,
                index == 0 ? ModFileCategory.Main : ModFileCategory.OldVersion,
                version.Size,
                version.UploadedAt,
                [],
                ThunderstoreLink.Page(mod.Game.Key, package.Name, version.Number),

                // Only forward in time: a version numbered higher but uploaded before, as for a branch kept up apart,
                // replaced nothing.
                newer is not null && newer.UploadedAt > version.UploadedAt ? new ModFileRef(mod, newer.Number) : null));
        }

        return files;
    }

    /// <summary>
    /// The name Thunderstore gives a version's zip, such as <c>BepInEx-BepInExPack-5.4.2305.zip</c>.
    /// </summary>
    public static string FileName(PackageName package, string version) => $"{package.FullName}-{version}.zip";
}
