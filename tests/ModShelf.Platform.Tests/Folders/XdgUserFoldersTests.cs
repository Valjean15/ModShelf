using ModShelf.Platform.Folders;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Folders;

/// <summary>
/// The Downloads folder as <c>user-dirs.dirs</c> names it, in a home and a configuration folder of the test's own.
/// </summary>
public sealed class XdgUserFoldersTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private string Home => Path.Combine(_folder.Path, "home");

    private string Config => Path.Combine(_folder.Path, "config");

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/Downloads\"", "Downloads")]
    [InlineData("XDG_DOWNLOAD_DIR=\"$HOME/Téléchargements\"", "Téléchargements")]
    [InlineData("  XDG_DOWNLOAD_DIR = \"$HOME/My Files/Incoming\"  ", "My Files/Incoming")]
    [InlineData("XDG_DOWNLOAD_DIR=$HOME/Bare", "Bare")]
    public void ReadsAFolderInTheHomeFolder(string line, string folder)
    {
        WriteDirs("# This file is written by xdg-user-dirs-update", "XDG_DESKTOP_DIR=\"$HOME/Desktop\"", line);

        Assert.Equal(Path.Combine(Home, folder), Folders().Downloads);
    }

    [Fact]
    public void ReadsAnAbsoluteFolder()
    {
        WriteDirs("XDG_DOWNLOAD_DIR=\"/data/incoming\"");

        Assert.Equal("/data/incoming", Folders().Downloads);
    }

    [Theory]
    [InlineData("XDG_DOWNLOAD_DIR=\"Downloads\"")]
    [InlineData("XDG_DOWNLOAD_DIR=\"$XDG_DATA_HOME/Downloads\"")]
    [InlineData("XDG_DOCUMENTS_DIR=\"$HOME/Documents\"")]
    [InlineData("# XDG_DOWNLOAD_DIR=\"$HOME/Elsewhere\"")]
    public void FallsBackToDownloadsInTheHomeFolderWhenTheFileDoesntNameOneItCanUse(string line)
    {
        WriteDirs(line);

        Assert.Equal(Path.Combine(Home, "Downloads"), Folders().Downloads);
    }

    [Fact]
    public void FallsBackToDownloadsInTheHomeFolderWithoutTheFile()
    {
        Assert.Equal(Path.Combine(Home, "Downloads"), Folders().Downloads);
    }

    [Fact]
    public void ThisSystemsDownloadsFolderIsAnAbsolutePath()
    {
        Assert.True(Path.IsPathFullyQualified(UserFolders.ForThisSystem().Downloads));
    }

    private XdgUserFolders Folders() => new(Home, Config);

    private void WriteDirs(params string[] lines)
    {
        Directory.CreateDirectory(Config);
        File.WriteAllLines(Path.Combine(Config, "user-dirs.dirs"), lines);
    }
}
