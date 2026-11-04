using ModShelf.Application.Library;
using ModShelf.Application.Settings;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;

namespace ModShelf.Application.Tests.Library;

/// <summary>
/// Where the library's files are: a game's own folder, or the library's root; and which folders can't be a game's.
/// </summary>
public sealed class LibraryRootsTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "ModShelf.Tests");
    private static readonly string Root = Path.Combine(Base, "Mods");
    private static readonly string StardewFolder = Path.Combine(Base, "Stardew");
    private static readonly GameRef Stardew = new(new SourceId("nexus"), "stardewvalley");
    private static readonly GameRef Skyrim = new(new SourceId("nexus"), "skyrimspecialedition");

    private readonly LibraryRoots _roots = new(Root, [KeyValuePair.Create(new GameRef(new SourceId("nexus"), "StardewValley"), StardewFolder)]);

    [Fact]
    public void AGameWithAFolderOfItsOwnIsThereInAnyCaseAndOthersAreInTheRoot()
    {
        Assert.Equal((StardewFolder, true), (_roots.For(Stardew), _roots.HasOwn(Stardew)));
        Assert.Equal((Root, false), (_roots.For(Skyrim), _roots.HasOwn(Skyrim)));
        Assert.Equal((Root, StardewFolder), (_roots.Root, Assert.Single(_roots.Games).Value));
        Assert.Equal(Root, _roots.For(new GameRef(new SourceId("example"), "stardewvalley")));
    }

    [Fact]
    public void ResolvesAndFindsAFileInItsGamesFolder()
    {
        LibraryFile file = new(
            new ModFileRef(new ModRef(Stardew, "2400"), "160380"), "SMAPI", null, ModFileCategory.Main, DateTimeOffset.UnixEpoch, "SMAPI (2400)/SMAPI.zip", 1, DateTimeOffset.UnixEpoch);
        string path = Path.Combine(StardewFolder, "SMAPI (2400)", "SMAPI.zip");

        Assert.Equal(path, _roots.Resolve(file));
        Assert.Equal(Path.Combine(Root, "a", "b.zip"), _roots.Resolve(Skyrim, "a/b.zip"));
        Assert.Equal("SMAPI (2400)/SMAPI.zip", _roots.RelativeTo(Stardew, path));
        Assert.Null(_roots.RelativeTo(Skyrim, path));
        Assert.Null(_roots.RelativeTo(Stardew, Path.Combine(Root, "SMAPI.zip")));
    }

    [Fact]
    public void TakesTheFoldersTheSettingsSay()
    {
        AppSettings settings = new()
        {
            Games = new Dictionary<GameRef, GameSettings>(AppSettings.GameComparer)
            {
                [Stardew] = new GameSettings { Root = StardewFolder },
                [Skyrim] = new GameSettings { Hidden = true },
            },
        };

        LibraryRoots byDefault = LibraryRoots.Of(settings, Root);
        LibraryRoots set = LibraryRoots.Of(settings with { LibraryRoot = Base }, Root);

        Assert.Equal((Root, StardewFolder, Root), (byDefault.Root, byDefault.For(Stardew), byDefault.For(Skyrim)));
        Assert.Equal(Base, set.Root);
        Assert.Single(byDefault.Games);
    }

    [Fact]
    public void RefusesFoldersThatArentAbsolute()
    {
        Assert.Throws<ArgumentException>(() => new LibraryRoots("Mods"));
        Assert.Throws<ArgumentException>(() => new LibraryRoots(Root, [KeyValuePair.Create(Stardew, "Stardew")]));
        Assert.Throws<ArgumentException>(() => new GameSettings { Root = "Stardew" });
        Assert.Throws<ArgumentException>(() => new GameSettings { Root = " " });
    }

    [Theory]
    [InlineData("Mods", "is the library's folder. Choose another folder.")]
    [InlineData("Mods/Stardew", "is inside the library's folder, {Root}. Choose a folder outside it.")]
    [InlineData("", "holds the library's folder, {Root}. Choose a folder that doesn't.")]
    public void AGamesFolderIsntTheLibrarysOrInsideItOrHoldingIt(string folder, string clash)
    {
        string path = Path.Combine(Base, folder.Replace('/', Path.DirectorySeparatorChar));
        path = Path.TrimEndingDirectorySeparator(path);

        Assert.Equal($"{path} {clash.Replace("{Root}", Root, StringComparison.Ordinal)}", _roots.ClashOf(Skyrim, path));
    }

    [Theory]
    [InlineData("Stardew", "is nexus/StardewValley's folder. Choose another folder.")]
    [InlineData("Stardew/Skyrim", "is inside nexus/StardewValley's folder, {Stardew}. Choose a folder outside it.")]
    public void AGamesFolderIsntAnotherGamesOrInsideItButMayBeInItsOwn(string folder, string clash)
    {
        string path = Path.Combine(Base, folder.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal($"{path} {clash.Replace("{Stardew}", StardewFolder, StringComparison.Ordinal)}", _roots.ClashOf(Skyrim, path));
        Assert.Null(_roots.ClashOf(Stardew, path));
    }

    [Fact]
    public void TheLibrarysRootIsntAGamesFolderOrInsideOneOrHoldingOne()
    {
        Assert.Equal($"{StardewFolder} is nexus/StardewValley's folder. Choose another folder.", _roots.ClashOf(null, StardewFolder));
        Assert.Equal(
            $"{Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)} holds nexus/StardewValley's folder, {StardewFolder}. Choose a folder that doesn't.",
            _roots.ClashOf(null, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
        Assert.Null(_roots.ClashOf(null, Path.Combine(Base, "Other")));
        Assert.Null(_roots.ClashOf(Skyrim, Path.Combine(Base, "Skyrim")));
    }
}
