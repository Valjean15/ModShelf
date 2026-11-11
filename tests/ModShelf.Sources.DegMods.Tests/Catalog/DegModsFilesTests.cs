using ModShelf.Domain.Mods;
using ModShelf.Sources.DegMods.Catalog;
using ModShelf.Sources.DegMods.Links;
using ModShelf.Sources.DegMods.Tests.Common;

namespace ModShelf.Sources.DegMods.Tests.Catalog;

public sealed class DegModsFilesTests
{
    private static readonly Uri Site = DegModsOptions.DefaultSiteAddress;
    private static readonly ModRef ModRef = new(new GameRef(new SourceId("degmods"), "fallout: new vegas"), "mod");

    [Fact]
    public void TheFirstFileIsTheMainOneTheOthersOptionalEachMadeWhenTheModsEventWas()
    {
        DegMod newspapers = Recorded(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers);

        IReadOnlyList<ModFile> files = DegModsFiles.Files(newspapers, ModRef, Site);

        Assert.Equal([ModFileCategory.Main, ModFileCategory.Optional, ModFileCategory.Optional], files.Select(file => file.Category));
        Assert.All(files, file => Assert.Equal(newspapers.UpdatedAt, file.UploadedAt));
        Assert.All(files, file => Assert.Null(file.SizeInBytes));
        Assert.All(files, file => Assert.Null(file.ReplacedBy));
        Assert.Equal(newspapers.Downloads.Select(download => download.Id), files.Select(file => file.Reference.Id));
        Assert.All(files, file => Assert.Equal(ModRef, file.Reference.Mod));
    }

    [Fact]
    public void EachFileHasItsSha256AndModShelfsLinkToIt()
    {
        DegMod newspapers = Recorded(RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers);

        ModFile first = DegModsFiles.Files(newspapers, ModRef, Site)[0];

        Assert.Equal(new FileHash(FileHashAlgorithm.Sha256, RecordedDegMods.NewspapersFile), Assert.Single(first.Hashes));
        Assert.Equal(DegModsLink.FilePage(Site, DegModsLink.ModKind, RecordedDegMods.FalloutAuthor, RecordedDegMods.Newspapers, first.Reference.Id), first.PageUrl);
        Assert.Equal("1.0", first.VersionLabel);
    }

    [Fact]
    public void AFileWithoutAHashHasNone()
    {
        DegMod fed = Recorded(RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration);

        ModFile file = Assert.Single(DegModsFiles.Files(fed, ModRef, Site));

        Assert.Empty(file.Hashes);
        Assert.Equal(DegModsLink.FilePage(Site, DegModsLink.OldModKind, RecordedDegMods.VgbAuthor, RecordedDegMods.FedAndHydration, file.Reference.Id), file.PageUrl);
    }

    [Theory]
    [InlineData("Main", null, "Main")]
    [InlineData(null, "main.zip", "main.zip")]
    [InlineData(null, null, "The mod")]
    public void AFilesNameIsItsTitleItsFileNameOrTheMods(string? title, string? fileName, string name)
    {
        DegMod mod = Mod(new DegDownload("0123456789abcdef", new Uri("https://files.catbox.moe/x.zip"), null, title, fileName, null));

        Assert.Equal(name, DegModsFiles.Name(mod, mod.Downloads[0], 0));
    }

    [Fact]
    public void AnOptionalFileWithoutATitleOrNameIsNamedAfterTheModAndItsPlace()
    {
        DegMod mod = Mod(new DegDownload("0123456789abcdef", new Uri("https://files.catbox.moe/x.zip"), null, null, null, null));

        Assert.Equal("The mod (3)", DegModsFiles.Name(mod, mod.Downloads[0], 2));
    }

    [Theory]
    [InlineData("https://files.catbox.moe/x.zip", null, null, "x.zip")]
    [InlineData("https://files.catbox.moe/x.zip", null, "given.7z", "given.7z")]
    [InlineData("https://files.catbox.moe/x.zip", null, "...", "x.zip")]
    [InlineData("https://archive.org/download/Item/Some%20File.7z", null, null, "Some File.7z")]
    [InlineData("https://brs.degmods.com/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.zip", "Main File", null, "Main File.zip")]
    [InlineData("https://brs.degmods.com/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, null, "The mod")]
    [InlineData("https://www.mediafire.com/file/abc/Halo.zip/file", null, null, "The mod")]
    [InlineData("https://files.catbox.moe/x.zip", null, "a/b:c.zip", "a_b_c.zip")]
    public void AFilesNameIsTheOneGivenOrItsLinksOrItsTitleWithItsLinksExtension(string link, string? title, string? fileName, string expected)
    {
        DegMod mod = Mod(new DegDownload("0123456789abcdef", new Uri(link), null, title, fileName, null));

        string saved = DegModsFiles.FileName(mod, mod.Downloads[0]);

        Assert.Equal(expected, saved);
        Assert.True(saved.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, saved);
    }

    [Fact]
    public void AFileWithNoUsableNameAtAllIsNamedByItsId()
    {
        DegMod mod = Mod(new DegDownload("0123456789abcdef", new Uri("https://example.com/"), null, null, null, null)) with { Title = "..." };

        Assert.Equal("0123456789abcdef", DegModsFiles.FileName(mod, mod.Downloads[0]));
    }

    private static DegMod Recorded(string author, string d)
        => ModEvents.Read(RecordedDegMods.RecordedEvents("brs.degmods.com").Concat(RecordedDegMods.RecordedEvents("relay.degmods.com")))
            .Single(mod => mod.Author == author && mod.D == d);

    private static DegMod Mod(DegDownload download)
        => new()
        {
            Kind = DegModsLink.ModKind,
            Author = new string('a', 64),
            D = "d",
            Title = "The mod",
            Game = "Game",
            Tags = new HashSet<string>(),
            PublishedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Downloads = [download],
        };
}
