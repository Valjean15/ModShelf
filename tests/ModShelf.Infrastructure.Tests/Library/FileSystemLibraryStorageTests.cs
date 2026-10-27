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
    public void RegistersTheFileSystemOnce()
    {
        ServiceCollection services = new();
        services.AddLibraryStorage();
        services.AddLibraryStorage();

        ServiceDescriptor storage = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILibraryStorage));
        Assert.IsType<FileSystemLibraryStorage>(storage.ImplementationInstance);
    }
}
