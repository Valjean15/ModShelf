using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Presentation.ViewModels.Downloads;

namespace ModShelf.Presentation.Tests.ViewModels.Downloads;

public sealed class DownloadNotesTests
{
    private static readonly DownloadJobSnapshot Job = TestData.Job("SkyUI_5_2_SE.7z", folder: "Mods");

    [Theory]
    [InlineData(true, "Downloading SkyUI_5_2_SE.7z into Mods.")]
    [InlineData(false, "Downloading SkyUI_5_2_SE.7z into Mods.")]
    public void ANewDownloadSaysWhereItGoes(bool onDownloadsScreen, string note)
        => Assert.Equal(note, DownloadNotes.Added(new LinkDownload(Job, AlreadyQueued: false), onDownloadsScreen));

    [Theory]
    [InlineData(true, "Continuing the download of SkyUI_5_2_SE.7z, added earlier.")]
    [InlineData(false, "It's on the Downloads screen already.")]
    public void ADownloadAddedBeforeSaysWhereItIs(bool onDownloadsScreen, string note)
        => Assert.Equal(note, DownloadNotes.Added(new LinkDownload(Job, AlreadyQueued: true), onDownloadsScreen));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFileSavedAlreadySaysWhere(bool onDownloadsScreen)
        => Assert.Equal(
            "SkyUI_5_2_SE.7z is saved in Mods already.",
            DownloadNotes.Added(new LinkDownload(Job, AlreadyQueued: false, AlreadySaved: true), onDownloadsScreen));

    [Fact]
    public void AFileInTheLibraryAlreadySaysWhere()
    {
        string path = Path.Combine("Library", "SkyUI", "SkyUI_5_2_SE.7z");

        Assert.Equal($"{path} is in the library already.", DownloadNotes.InLibraryAlready(new LibraryDownload(path, Queued: null)));
    }
}
