using ModShelf.Application.Contents;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Tests.Common;
using ModShelf.Presentation.Tests.Fakes;
using ModShelf.Presentation.ViewModels.Dialogs;
using ModShelf.Testing;

namespace ModShelf.Presentation.Tests.ViewModels.Dialogs;

/// <summary>
/// Show contents (ADR 0106): an archive's tree, its folders opened and closed, and its text files read, its readme first.
/// </summary>
public sealed class FileContentsViewModelTests
{
    private static readonly ModFileRef File = new(TestData.Mod("12604"), "35407");

    private readonly ScriptedContents _contents = new();
    private readonly RecordingLogger<FileContentsViewModel> _logger = new();

    [Fact]
    public async Task ShowsTheTopFolderOpenWithTheReadmeChosenAndRead()
    {
        using CultureScope culture = new("en-US");
        List(
            Readme: "SkyUI/Readme.txt",
            ("SkyUI", 0, true),
            ("SkyUI/Interface", 0, true),
            ("SkyUI/Interface/skyui", 0, true),
            ("SkyUI/Interface/skyui/icons.swf", 3000, false),
            ("SkyUI/Readme.txt", 120, false),
            ("SkyUI/SkyUI_SE.bsa", 2_000_000, false));
        _contents.Texts[(File, "SkyUI/Readme.txt")] = new ArchiveText("SkyUI/Readme.txt", "SkyUI 5.2", 120, IsCut: false);
        FileContentsViewModel shown = Show();

        await shown.Listing;
        await shown.Reading;

        Assert.Equal("What's in SkyUI_5_2_SE.7z", shown.Title);
        Assert.Equal("3 files in 3 folders, 1.91 MB unpacked, in a 7z archive. Nothing is extracted.", shown.SummaryText);
        Assert.Equal(
            [("SkyUI", 0, true, "1.91 MB"), ("Interface", 1, true, "2.93 KB"), ("Readme.txt", 1, false, "120 B"), ("SkyUI_SE.bsa", 1, false, "1.91 MB")],
            shown.Rows.Select(row => (row.Name, row.Depth, row.IsFolder, row.SizeText)));
        Assert.Equal("SkyUI/Readme.txt", shown.SelectedRow?.Path);
        Assert.Equal(("SkyUI/Readme.txt", "SkyUI 5.2", (string?)null), (shown.TextTitle, shown.TextContent, shown.TextNote));
        Assert.True(shown.IsListed);
    }

    [Fact]
    public async Task FoldersOpenAndCloseOneAtATimeOrAllTogether()
    {
        List(
            Readme: null,
            ("Data", 0, true),
            ("Data/Textures", 0, true),
            ("Data/Textures/lantern.dds", 10, false),
            ("Data/Lanterns.esp", 20, false),
            ("Notes.md", 5, false));
        FileContentsViewModel shown = Show();
        await shown.Listing;
        string[] first = Names(shown);

        shown.Rows[0].ToggleCommand.Execute(null);
        string[] opened = Names(shown);
        shown.Rows[1].ToggleCommand.Execute(null);
        string[] deeper = Names(shown);
        shown.Rows[0].ToggleCommand.Execute(null);
        string[] closed = Names(shown);
        shown.ToggleAllCommand.Execute(null);
        string[] all = Names(shown);
        shown.ToggleAllCommand.Execute(null);

        // Two things at the top, so nothing opens by itself; a folder keeps what's open in it while it's closed.
        Assert.Equal(["Data", "Notes.md"], first);
        Assert.Equal(["Data", "Textures", "Lanterns.esp", "Notes.md"], opened);
        Assert.Equal(["Data", "Textures", "lantern.dds", "Lanterns.esp", "Notes.md"], deeper);
        Assert.Equal(["Data", "Notes.md"], closed);
        Assert.Equal(deeper, all);
        Assert.Equal(["Data", "Notes.md"], Names(shown));
    }

    [Fact]
    public async Task ChoosingATextFileReadsItAndSaysWhenOnlyItsStartIsShown()
    {
        using CultureScope culture = new("en-US");
        List(Readme: null, ("Big.txt", 1_000_000, false), ("Plugin.esp", 10, false));
        _contents.Texts[(File, "Big.txt")] = new ArchiveText("Big.txt", "aaa", 1_000_000, IsCut: true);
        FileContentsViewModel shown = Show();
        await shown.Listing;

        shown.SelectedRow = shown.Rows[1];
        bool readPlugin = _contents.Read.Count > 0;
        shown.SelectedRow = shown.Rows[0];
        await shown.Reading;

        Assert.False(readPlugin);
        Assert.Equal(("Big.txt", "aaa", "Only its first 256 KB is shown: it's 977 KB."), (shown.TextTitle, shown.TextContent, shown.TextNote));
    }

    [Fact]
    public async Task ATextFileChosenMeanwhileWins()
    {
        List(Readme: null, ("A.txt", 1, false), ("B.txt", 1, false));
        _contents.Texts[(File, "A.txt")] = new ArchiveText("A.txt", "first", 1, IsCut: false);
        _contents.Texts[(File, "B.txt")] = new ArchiveText("B.txt", "second", 1, IsCut: false);
        FileContentsViewModel shown = Show();
        await shown.Listing;
        _contents.ReadHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        shown.SelectedRow = shown.Rows[0];
        Task first = shown.Reading;
        _contents.ReadHeld = null;
        shown.SelectedRow = shown.Rows[1];
        await shown.Reading;
        await first;

        Assert.Equal(("B.txt", "second"), (shown.TextTitle, shown.TextContent));
    }

    [Theory]
    [InlineData(ArchiveTrouble.Encrypted, "It needs a password, which ModShelf doesn't ask for.")]
    [InlineData(ArchiveTrouble.Broken, "It can't be read: it's damaged, cut short, or isn't the kind of archive its name says.")]
    [InlineData(ArchiveTrouble.NotAnArchive, "ModShelf shows what .zip, .7z and .rar archives hold, not other files.")]
    public async Task SaysWhyAnArchiveCantBeShown(ArchiveTrouble trouble, string said)
    {
        _contents.ListFailure = new ArchiveUnreadableException(trouble, "Can't.");
        FileContentsViewModel shown = Show();

        await shown.Listing;

        Assert.Equal((said, false, false), (shown.Problem, shown.IsListed, shown.IsListing));
        Assert.Empty(shown.Rows);
        Assert.Contains(_logger.Entries, entry => entry.Contains("Couldn't list what an archive holds", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaysWhenTheArchiveLeftTheLibrarysFolder()
    {
        _contents.ListFailure = new FileNotFoundException("Gone.");
        FileContentsViewModel shown = Show();

        await shown.Listing;

        Assert.Equal("The file isn't in the library's folder any more. Rescan the library if it was moved.", shown.Problem);
    }

    [Fact]
    public async Task SaysWhyATextFileCantBeShown()
    {
        List(Readme: "Readme.txt", ("Readme.txt", 10, false));
        _contents.ReadFailure = new ArchiveUnreadableException(ArchiveTrouble.Encrypted, "Can't.");
        FileContentsViewModel shown = Show();

        await shown.Listing;
        await shown.Reading;

        Assert.Equal(("Readme.txt", string.Empty, "It needs a password, which ModShelf doesn't ask for."), (shown.TextTitle, shown.TextContent, shown.TextNote));
        Assert.True(shown.IsListed);
    }

    [Fact]
    public async Task AHundredThousandFilesShowOnlyTheRowsOfTheFoldersOpen()
    {
        using CultureScope culture = new("en-US");
        List<(string Path, long Size, bool IsFolder)> entries = [("Mod", 0, true)];
        for (int folder = 0; folder < 100; folder++)
        {
            entries.Add(($"Mod/Folder {folder:D3}", 0, true));
            entries.AddRange(Enumerable.Range(0, 1000).Select(file => ($"Mod/Folder {folder:D3}/File {file:D4}.dds", 1L, false)));
        }

        List(null, [.. entries]);
        FileContentsViewModel shown = Show();

        await shown.Listing;
        shown.Rows[1].ToggleCommand.Execute(null);

        // The top folder, open by itself, its 100 folders, and the 1,000 files of the one opened.
        Assert.Equal(1 + 100 + 1000, shown.Rows.Count);
        Assert.Equal(("Mod", "97.7 KB", "1000 B"), (shown.Rows[0].Name, shown.Rows[0].SizeText, shown.Rows[1].SizeText));
    }

    [Fact]
    public void ClosingAsksTheDialogToClose()
    {
        FileContentsViewModel shown = Show();
        int asked = 0;
        shown.CloseRequested += (_, _) => asked++;

        shown.CloseCommand.Execute(null);

        Assert.Equal(1, asked);
    }

    private static string[] Names(FileContentsViewModel shown) => [.. shown.Rows.Select(row => row.Name)];

    private void List(string? Readme, params (string Path, long Size, bool IsFolder)[] entries)
    {
        ArchiveEntry[] listed = [.. entries.Select(entry => new ArchiveEntry(entry.Path, entry.Size, entry.IsFolder))];
        _contents.Listings[File] = new ArchiveContents(
            File, "SkyUI_5_2_SE.7z", "7z", listed, listed.Count(entry => !entry.IsFolder), listed.Sum(entry => entry.SizeInBytes), Readme);
    }

    private FileContentsViewModel Show()
    {
        FileContentsViewModel shown = new(_contents, File, "SkyUI_5_2_SE.7z", _logger);
        shown.StartListing();
        return shown;
    }
}
