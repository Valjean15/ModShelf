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
    public void TellsTheRoomOnTheDriveOfAFolderThatNeedntExistYet()
    {
        FileSystemLibraryStorage storage = new();

        long? free = storage.FreeSpace(Path.Combine(_folder.Path, "Not made", "yet"));

        Assert.NotNull(free);
        Assert.True(free > 0);
        Assert.Throws<ArgumentException>(() => storage.FreeSpace(" "));
    }

    [Fact]
    public void FoldersOnOneDriveAreOnTheSameDrive()
    {
        FileSystemLibraryStorage storage = new();
        string made = Path.Combine(_folder.Path, "Made");
        Directory.CreateDirectory(made);

        Assert.True(storage.SameDrive(made, Path.Combine(_folder.Path, "Not made", "yet")));
        Assert.Throws<ArgumentException>(() => storage.SameDrive(made, " "));
    }

    [Fact]
    public void FoldersOnTwoDrivesAreNotOnTheSameDrive()
    {
        // Linux keeps the kernel's processes on a file system of its own, mounted at /proc.
        Assert.SkipUnless(OperatingSystem.IsLinux() && Directory.Exists("/proc/self"), "Needs Linux's /proc.");
        FileSystemLibraryStorage storage = new();

        Assert.False(storage.SameDrive("/proc/self", _folder.Path));
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
    public void ListsTheFilesInAFolderButDownloadsUnderWay()
    {
        string folder = Path.Combine(_folder.Path, "SkyUI (12604)");
        Directory.CreateDirectory(Path.Combine(folder, "_archive"));
        foreach (string name in new[] { "SkyUI.7z", "Readme.txt", "SkyUI 5.3.7z.part", "SkyUI 5.3.7z.part.info", ".modshelf-0f3c.tmp" })
        {
            File.WriteAllText(Path.Combine(folder, name), "x");
        }

        File.WriteAllText(Path.Combine(folder, "_archive", "SkyUI 5.1.7z"), "x");
        FileSystemLibraryStorage storage = new();

        Assert.Equal([Path.Combine(folder, "Readme.txt"), Path.Combine(folder, "SkyUI.7z")], storage.FilesIn(folder));
        Assert.Empty(storage.FilesIn(Path.Combine(_folder.Path, "Nothing here")));
    }

    [Fact]
    public async Task ReadsAFilesSha256()
    {
        // The SHA-256 of "abc", from FIPS 180-2.
        string file = Path.Combine(_folder.Path, "SkyUI.7z");
        await File.WriteAllTextAsync(file, "abc", TestContext.Current.CancellationToken);
        FileSystemLibraryStorage storage = new();

        string sha256 = await storage.Sha256Async(file, TestContext.Current.CancellationToken);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", sha256);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.Sha256Async(Path.Combine(_folder.Path, "SKSE.7z"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadsAFilesSha256AndMd5AtOnce()
    {
        // The hashes of "abc", from FIPS 180-2 and RFC 1321.
        string file = Path.Combine(_folder.Path, "SkyUI.7z");
        await File.WriteAllTextAsync(file, "abc", TestContext.Current.CancellationToken);
        FileSystemLibraryStorage storage = new();

        FileHashes hashes = await storage.HashesAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(
            new FileHashes("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "900150983cd24fb0d6963f7d28e17f72"),
            hashes);
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.HashesAsync(Path.Combine(_folder.Path, "SKSE.7z"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadsASmallFileWholeButNotALargerOne()
    {
        string note = Path.Combine(_folder.Path, "SkyUI.7z.meta");
        await File.WriteAllTextAsync(note, "[General]\nmodID=12604\n", TestContext.Current.CancellationToken);
        FileSystemLibraryStorage storage = new();

        byte[] read = await storage.ReadSmallFileAsync(note, 64, TestContext.Current.CancellationToken);

        Assert.Equal(await File.ReadAllBytesAsync(note, TestContext.Current.CancellationToken), read);
        await Assert.ThrowsAsync<IOException>(() => storage.ReadSmallFileAsync(note, 8, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.ReadSmallFileAsync(Path.Combine(_folder.Path, "SKSE.7z.meta"), 64, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopiesAFileIntoFoldersItMakesLeavingTheOriginal()
    {
        byte[] content = new byte[3 * 1024 * 1024 + 17];
        Random.Shared.NextBytes(content);
        string from = Path.Combine(_folder.Path, "Downloads", "SkyUI.7z");
        string to = Path.Combine(_folder.Path, "Library", "SkyUI (12604)", "SkyUI.7z");
        Directory.CreateDirectory(Path.GetDirectoryName(from)!);
        await File.WriteAllBytesAsync(from, content, TestContext.Current.CancellationToken);
        FileSystemLibraryStorage storage = new();
        string sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

        await storage.CopyFileAsync(from, to, sha256.ToUpperInvariant(), TestContext.Current.CancellationToken);

        Assert.Equal(content, await File.ReadAllBytesAsync(from, TestContext.Current.CancellationToken));
        Assert.Equal(content, await File.ReadAllBytesAsync(to, TestContext.Current.CancellationToken));
        Assert.Equal([to], Directory.GetFiles(Path.GetDirectoryName(to)!));
    }

    [Fact]
    public async Task NeverCopiesOverAFileNorKeepsACopyThatChangedOrStopped()
    {
        string from = Path.Combine(_folder.Path, "SkyUI.7z");
        string taken = Path.Combine(_folder.Path, "Library", "Taken.7z");
        string to = Path.Combine(_folder.Path, "Library", "SkyUI.7z");
        await File.WriteAllTextAsync(from, "abc", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(taken)!);
        await File.WriteAllTextAsync(taken, "theirs", TestContext.Current.CancellationToken);
        FileSystemLibraryStorage storage = new();
        const string Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        using CancellationTokenSource stopped = new();
        await stopped.CancelAsync();

        await Assert.ThrowsAsync<IOException>(() => storage.CopyFileAsync(from, taken, Abc, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.CopyFileAsync(from, to, new string('0', 64), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CopyFileAsync(from, to, Abc, stopped.Token));
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.CopyFileAsync(Path.Combine(_folder.Path, "SKSE.7z"), to, Abc, TestContext.Current.CancellationToken));

        Assert.Equal("theirs", await File.ReadAllTextAsync(taken, TestContext.Current.CancellationToken));
        Assert.Equal([taken], Directory.GetFiles(Path.GetDirectoryName(to)!));
        Assert.Equal("abc", await File.ReadAllTextAsync(from, TestContext.Current.CancellationToken));
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
    public void AFolderBehindALinkIsTheSameFolder()
    {
        (string mods, string link) = LinkedFolders();
        FileSystemLibraryStorage storage = new();

        Assert.True(storage.IsSameFolder(mods, link));
        Assert.True(storage.IsSameFolder(link, mods));
        Assert.Empty(Directory.EnumerateFileSystemEntries(mods));
    }

    [Fact]
    public void AnotherCaseIsTheSameFolderOnlyWhereTheFileSystemIgnoresCase()
    {
        string mods = Path.Combine(_folder.Path, "Mods");
        string upper = Path.Combine(_folder.Path, "MODS");
        Directory.CreateDirectory(mods);
        FileSystemLibraryStorage storage = new();

        // Wherever the file system ignores case, the other case finds the folder.
        Assert.Equal(Directory.Exists(upper), storage.IsSameFolder(mods, upper));
        Assert.Empty(Directory.EnumerateFileSystemEntries(mods));
    }

    [Fact]
    public void TwoFoldersAreNotTheSameAndAMissingOneIsNeither()
    {
        string mods = Path.Combine(_folder.Path, "Mods");
        string other = Path.Combine(_folder.Path, "Other");
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(other);
        FileSystemLibraryStorage storage = new();

        Assert.False(storage.IsSameFolder(mods, other));
        Assert.False(storage.IsSameFolder(mods, Path.Combine(_folder.Path, "Missing")));
        Assert.False(storage.IsSameFolder(Path.Combine(_folder.Path, "Missing"), mods));
        Assert.Empty(Directory.EnumerateFileSystemEntries(mods));
    }

    [Fact]
    public void AFolderItCantMarkIsComparedByWhereItsLinksLead()
    {
        (string mods, string link) = LinkedFolders();
        string other = Path.Combine(_folder.Path, "Other");
        Directory.CreateDirectory(other);

        // A folder in the mark's place, so it can't be made.
        Directory.CreateDirectory(Path.Combine(mods, "taken"));
        Directory.CreateDirectory(Path.Combine(other, "taken"));
        FileSystemLibraryStorage storage = new(OperatingSystem.IsWindows(), () => "taken");

        Assert.True(storage.IsSameFolder(mods, link));
        Assert.False(storage.IsSameFolder(other, mods));
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

    /// <summary>
    /// A folder, and a link to it; skips the test where links can't be made.
    /// </summary>
    private (string Folder, string Link) LinkedFolders()
    {
        string mods = Path.Combine(_folder.Path, "Mods");
        string link = Path.Combine(_folder.Path, "Library");
        Directory.CreateDirectory(mods);
        try
        {
            Directory.CreateSymbolicLink(link, mods);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("This system doesn't let the tests make links.");
        }

        return (mods, link);
    }
}
