using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Library.Ports;
using ModShelf.Infrastructure.Library;
using ModShelf.Testing;

namespace ModShelf.Infrastructure.Tests.Library;

public sealed class FileSystemLibraryStorageTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void FindsOnlyFiles()
    {
        string file = Path.Combine(_folder.Path, "SkyUI.7z");
        File.WriteAllText(file, "7z");
        FileSystemLibraryStorage storage = new();

        Assert.True(storage.FileExists(file));
        Assert.False(storage.FileExists(Path.Combine(_folder.Path, "SKSE.7z")));
        Assert.False(storage.FileExists(_folder.Path));
    }

    [Fact]
    public void OnWindowsAPathLeavesRoomForTheDownloadsLongerName()
    {
        FileSystemLibraryStorage windows = new(isWindows: true);
        string longest = @"C:\" + new string('m', 254 - 3);
        string tooLong = longest + "m";

        windows.EnsurePathFits(longest);
        PathTooLongException refused = Assert.Throws<PathTooLongException>(() => windows.EnsurePathFits(tooLong));

        Assert.Equal(
            $"{tooLong} is too long for Windows, where a file in the library can have 254 characters at most. Choose a "
            + "shorter library root, or a folder pattern with fewer or shorter parts.",
            refused.Message);
    }

    [Fact]
    public void ElsewhereALongPathFits()
    {
        new FileSystemLibraryStorage(isWindows: false).EnsurePathFits(Path.Combine(_folder.Path, new string('m', 1000)));
    }

    [Fact]
    public void TellsAFilesSize()
    {
        string file = Path.Combine(_folder.Path, "SkyUI.7z");
        File.WriteAllBytes(file, new byte[1234]);
        FileSystemLibraryStorage storage = new();

        Assert.Equal(1234, storage.FileSize(file));
        Assert.Null(storage.FileSize(Path.Combine(_folder.Path, "SKSE.7z")));
        Assert.Null(storage.FileSize(_folder.Path));
    }

    [Fact]
    public void MovesAFileIntoFoldersItMakes()
    {
        string from = Path.Combine(_folder.Path, "SkyUI.7z");
        string to = Path.Combine(_folder.Path, "Mods", "_archive", "SkyUI.7z");
        File.WriteAllText(from, "7z");

        new FileSystemLibraryStorage().MoveFile(from, to);

        Assert.False(File.Exists(from));
        Assert.Equal("7z", File.ReadAllText(to));
    }

    [Fact]
    public void NeverMovesAFileOverAnother()
    {
        string from = Path.Combine(_folder.Path, "SkyUI.7z");
        string to = Path.Combine(_folder.Path, "Other.7z");
        File.WriteAllText(from, "mine");
        File.WriteAllText(to, "theirs");

        Assert.Throws<IOException>(() => new FileSystemLibraryStorage().MoveFile(from, to));

        Assert.Equal(("mine", "theirs"), (File.ReadAllText(from), File.ReadAllText(to)));
    }

    [Fact]
    public void DeletesAFileOrNothing()
    {
        string file = Path.Combine(_folder.Path, "SkyUI.7z");
        File.WriteAllText(file, "7z");
        FileSystemLibraryStorage storage = new();

        storage.DeleteFile(file);
        storage.DeleteFile(file);
        storage.DeleteFile(Path.Combine(_folder.Path, "Gone", "SkyUI.7z"));

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void RemovesOnlyAnEmptyFolder()
    {
        string empty = Path.Combine(_folder.Path, "Empty");
        string full = Path.Combine(_folder.Path, "Full");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(Path.Combine(full, "Inner"));
        FileSystemLibraryStorage storage = new();

        Assert.True(storage.RemoveFolderIfEmpty(empty));
        Assert.False(storage.RemoveFolderIfEmpty(full));
        Assert.False(storage.RemoveFolderIfEmpty(Path.Combine(_folder.Path, "Missing")));

        Assert.False(Directory.Exists(empty));
        Assert.True(Directory.Exists(full));
    }

    [Fact]
    public void RegistersTheFileSystemOnce()
    {
        ServiceCollection services = new();
        services.AddLibraryStorage();
        services.AddLibraryStorage();

        ServiceDescriptor storage = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILibraryStorage));
        Assert.IsType<FileSystemLibraryStorage>(storage.ImplementationInstance);
    }
}
