using System.Text.Json;

using ModShelf.Domain.Mods;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Catalog;
using ModShelf.Sources.CurseForge.Tests.Common;

namespace ModShelf.Sources.CurseForge.Tests.Catalog;

/// <summary>
/// A mod's files on CurseForge as ModShelf's: builds side by side, each updated only by a newer build of its kind.
/// </summary>
public sealed class CurseForgeFilesTests
{
    private static readonly ModRef Mod = new(new GameRef(new SourceId("curseforge"), "minecraft"), "308702");
    private static readonly Uri Page = new(TestCurseForge.ModPage);
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AFilesUpdateIsTheNewerBuildForTheSameGameVersionsAndKind()
    {
        CurseForgeFile[] files =
        [
            File(TestCurseForge.File(1, 308702, "a-1.20.jar", Day, ["1.20.1", "Fabric"])),
            File(TestCurseForge.File(2, 308702, "a-1.21.jar", Day.AddDays(1), ["1.21", "Fabric"])),
            File(TestCurseForge.File(3, 308702, "b-1.20.jar", Day.AddDays(2), ["Fabric", "1.20.1"])),
            File(TestCurseForge.File(4, 308702, "c-1.20-beta.jar", Day.AddDays(3), ["1.20.1", "Fabric"], releaseType: 2)),
            File(TestCurseForge.File(5, 308702, "d-1.20.jar", Day.AddDays(4), ["1.20.1", "Fabric"], status: 8)),
        ];

        IReadOnlyList<ModFile> listed = CurseForgeFiles.Of(Mod, Page, files);

        Assert.Equal(["5", "4", "3", "2", "1"], listed.Select(file => file.Reference.Id));
        Assert.Equal(
            [
                ("5", ModFileCategory.Archived, (string?)null),
                ("4", ModFileCategory.Optional, null),
                ("3", ModFileCategory.Main, null),
                ("2", ModFileCategory.Main, null),
                ("1", ModFileCategory.Main, "3"),
            ],
            listed.Select(file => (file.Reference.Id, file.Category, file.ReplacedBy?.Id)));
    }

    [Fact]
    public void KeepsTheFilesNameSizeDateHashesAndPage()
    {
        ModFile file = CurseForgeFiles.Of(Mod, Page, File(TestCurseForge.File(5432112, 308702, "modmenu-11.0.1.jar", Day)));

        Assert.Equal(
            ("Display modmenu-11.0.1.jar", "modmenu-11.0.1.jar", (string?)null, 5433112L, Day),
            (file.Name, file.FileName, file.VersionLabel, file.SizeInBytes, file.UploadedAt));
        Assert.Equal([new FileHash(FileHashAlgorithm.Sha1, new string('a', 40)), new FileHash(FileHashAlgorithm.Md5, new string('b', 32))], file.Hashes);
        Assert.Equal(new Uri("https://www.curseforge.com/minecraft/mc-mods/modmenu/files/5432112"), file.PageUrl);
    }

    [Fact]
    public void LeavesOutAHashThatIsntWhatItSays()
    {
        CurseForgeFile file = File(TestCurseForge.File(1, 308702, "a.jar", Day)) with
        {
            Hashes = [new CurseForgeHash("not a hash", 1), new CurseForgeHash(new string('c', 32), 2), new CurseForgeHash(new string('d', 64), 3)],
        };

        Assert.Equal([new FileHash(FileHashAlgorithm.Md5, new string('c', 32))], CurseForgeFiles.Hashes(file));
    }

    private static CurseForgeFile File(System.Text.Json.Nodes.JsonObject json)
        => json.Deserialize<CurseForgeFile>(CurseForgeApiClient.Json)!;
}
