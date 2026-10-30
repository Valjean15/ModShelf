using ModShelf.Platform.Links;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// One key of a <c>mimeapps.list</c>, read and changed with the rest of the file left as it was.
/// </summary>
public sealed class MimeAppsListTests : IDisposable
{
    private const string Key = "x-scheme-handler/nxm";

    private readonly TempFolder _folder = new();

    private string File => Path.Combine(_folder.Path, "config", "mimeapps.list");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void ReadsTheKeyInItsSectionOnly()
    {
        Write("[Added Associations]", $"{Key}=added.desktop;", string.Empty, "[Default Applications]", "text/plain=gedit.desktop", $" {Key} = vortex.desktop; ");

        Assert.Equal("vortex.desktop;", MimeAppsList.Read(File, MimeAppsList.DefaultApplications, Key));
        Assert.Equal("added.desktop;", MimeAppsList.Read(File, "Added Associations", Key));
        Assert.Null(MimeAppsList.Read(File, "Removed Associations", Key));
        Assert.Null(MimeAppsList.Read(Path.Combine(_folder.Path, "missing", "mimeapps.list"), MimeAppsList.DefaultApplications, Key));
    }

    [Fact]
    public void IgnoresComments()
    {
        Write("[Default Applications]", $"# {Key}=commented.desktop");

        Assert.Null(MimeAppsList.Read(File, MimeAppsList.DefaultApplications, Key));
    }

    [Fact]
    public void ChangesTheKeyWhereItIs()
    {
        Write("# Mine", "[Default Applications]", "text/plain=gedit.desktop", $"{Key}=vortex.desktop;", "image/png=eog.desktop", string.Empty, "[Added Associations]", "a=b");

        MimeAppsList.Write(File, MimeAppsList.DefaultApplications, Key, "modshelf-nxm.desktop;");

        Assert.Equal(
            ["# Mine", "[Default Applications]", "text/plain=gedit.desktop", $"{Key}=modshelf-nxm.desktop;", "image/png=eog.desktop", string.Empty, "[Added Associations]", "a=b"],
            Lines());
    }

    [Fact]
    public void AddsTheKeyAtTheEndOfItsSection()
    {
        Write("[Default Applications]", "text/plain=gedit.desktop", string.Empty, "[Added Associations]", "a=b");

        MimeAppsList.Write(File, MimeAppsList.DefaultApplications, Key, "modshelf-nxm.desktop;");

        Assert.Equal(
            ["[Default Applications]", "text/plain=gedit.desktop", $"{Key}=modshelf-nxm.desktop;", string.Empty, "[Added Associations]", "a=b"],
            Lines());
    }

    [Fact]
    public void AddsTheSectionAndTheFileWhenMissing()
    {
        MimeAppsList.Write(File, MimeAppsList.DefaultApplications, Key, "modshelf-nxm.desktop;");

        Assert.Equal(["[Default Applications]", $"{Key}=modshelf-nxm.desktop;"], Lines());

        Write("[Added Associations]", "a=b");
        MimeAppsList.Write(File, MimeAppsList.DefaultApplications, Key, "modshelf-nxm.desktop;");

        Assert.Equal(["[Added Associations]", "a=b", string.Empty, "[Default Applications]", $"{Key}=modshelf-nxm.desktop;"], Lines());
    }

    [Fact]
    public void RemovesOnlyTheKey()
    {
        Write("[Default Applications]", $"{Key}=modshelf-nxm.desktop;", "text/plain=gedit.desktop");

        MimeAppsList.Write(File, MimeAppsList.DefaultApplications, Key, null);
        MimeAppsList.Write(Path.Combine(_folder.Path, "missing", "mimeapps.list"), MimeAppsList.DefaultApplications, Key, null);

        Assert.Equal(["[Default Applications]", "text/plain=gedit.desktop"], Lines());
        Assert.False(Directory.Exists(Path.Combine(_folder.Path, "missing")));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void KeepsTheFilesLineEndings(string newLine)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, string.Join(newLine, "# Mine", "[Default Applications]", $"{Key}=vortex.desktop;", "text/plain=gedit.desktop") + newLine);

        MimeAppsList.Write(File, MimeAppsList.DefaultApplications, Key, "modshelf-nxm.desktop;");

        Assert.Equal(
            string.Join(newLine, "# Mine", "[Default Applications]", $"{Key}=modshelf-nxm.desktop;", "text/plain=gedit.desktop") + newLine,
            System.IO.File.ReadAllText(File));
    }

    [Fact]
    public void AnEmptyFileGetsTheSection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, string.Empty);

        MimeAppsList.Write(File, MimeAppsList.DefaultApplications, Key, "modshelf-nxm.desktop;");

        Assert.Equal(["[Default Applications]", $"{Key}=modshelf-nxm.desktop;"], Lines());
    }

    private void Write(params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, string.Join('\n', lines) + "\n");
    }

    private string[] Lines() => System.IO.File.ReadAllText(File).TrimEnd('\n').Split('\n');
}
