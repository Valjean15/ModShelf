using ModShelf.Application.Library;

namespace ModShelf.Application.Tests.Library;

public sealed class LibraryLocationsTests
{
    [Theory]
    [InlineData("SkyUI_5_2_SE.7z", "SkyUI_5_2_SE (35407).7z")]
    [InlineData("README", "README (35407)")]
    [InlineData("SkyUI.tar.gz", "SkyUI.tar (35407).gz")]
    public void ANameWithTheFilesIdHasItBeforeTheExtension(string name, string withId)
    {
        Assert.Equal(withId, LibraryLocations.WithId(name, "35407"));
    }

    [Theory]
    [InlineData("nexus/skyrim/SkyUI (12604)/SkyUI.7z", "nexus/skyrim/SkyUI (12604)", "nexus/skyrim/SkyUI (12604)", "SkyUI.7z")]
    [InlineData("nexus/skyrim/SkyUI (12604)/_archive/SkyUI.7z", "nexus/skyrim/SkyUI (12604)/_archive", "nexus/skyrim/SkyUI (12604)", "SkyUI.7z")]
    [InlineData("SkyUI.7z", "", "", "SkyUI.7z")]
    public void AModsFolderHoldsItsArchive(string relativePath, string folder, string modFolder, string name)
    {
        Assert.Equal(
            (folder, modFolder, name),
            (LibraryLocations.FolderOf(relativePath), LibraryLocations.ModFolderOf(relativePath), LibraryLocations.NameOf(relativePath)));
    }
}
