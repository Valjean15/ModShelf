using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;

namespace ModShelf.Presentation.Tests.Common;

/// <summary>
/// What an update that replaces none of a mod's files is called on its page and in the Library.
/// </summary>
public sealed class NewFilesTests
{
    private static readonly ModRef SkyUi = TestData.Mod("12604");

    [Fact]
    public void AnOptionalFileIsANewOptionalFile()
    {
        Assert.Equal("New optional file", NewFiles.Note(Update(ModFileCategory.Optional), WithFiles()));
    }

    [Fact]
    public void AMainFileOfAModWithFilesIsANewFile()
    {
        Assert.Equal("New file", NewFiles.Note(Update(ModFileCategory.Main), WithFiles()));
    }

    [Fact]
    public void AMainFileOfAModOnlyWatchedIsAnUpdate()
    {
        Assert.Null(NewFiles.Note(Update(ModFileCategory.Main), TestData.LibraryMod(SkyUi, "SkyUI", watched: true)));
    }

    [Fact]
    public void AMainFileOfAModWithOnlyArchivedVersionsIsAnUpdate()
    {
        LibraryMod archivedOnly = TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "_archive/a.7z"));

        Assert.Null(NewFiles.Note(Update(ModFileCategory.Main), archivedOnly));
    }

    [Fact]
    public void AFileThatReplacesOneIsAnUpdate()
    {
        LibraryUpdate update = new(new ModFileRef(SkyUi, "2"), "SkyUI 5.2", "b.7z", "5.2", ModFileCategory.Optional, TestData.Now, new ModFileRef(SkyUi, "1"), TestData.Now);

        Assert.Null(NewFiles.Note(update, WithFiles()));
    }

    private static LibraryUpdate Update(ModFileCategory category)
        => new(new ModFileRef(SkyUi, "2"), "Extra", "extra.7z", "1.0", category, TestData.Now, null, TestData.Now);

    private static LibraryMod WithFiles() => TestData.LibraryMod(SkyUi, "SkyUI", watched: true, TestData.LibraryFile(SkyUi, "1", "a.7z"));
}
