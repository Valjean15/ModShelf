using ModShelf.Platform.Installed;
using ModShelf.Platform.Tests.Links;

namespace ModShelf.Platform.Tests.Installed;

public sealed class SteamFoldersTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "steam-folders"));

    private readonly MemoryRegistryTree _user = new();
    private readonly MemoryRegistryTree _computer = new();

    [Fact]
    public void OnWindowsLooksWhereTheUsersRegistrySaysThenTheComputersThenTheDefault()
    {
        string own = Path.Combine(Root, "Own Steam");
        string shared = Path.Combine(Root, "Shared Steam");
        string shared64 = Path.Combine(Root, "Steam 64");
        string programs = Path.Combine(Root, "Programs");
        _user.Set(@"Software\Valve\Steam", "SteamPath", own.Replace('\\', '/'));
        _computer.Set(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", shared);
        _computer.Set(@"SOFTWARE\Valve\Steam", "InstallPath", shared64);

        IReadOnlyList<string> folders = SteamFolders.OnWindows(_user, _computer, programs);

        Assert.Equal([own, shared, shared64, Path.Combine(programs, "Steam")], folders);
    }

    [Fact]
    public void OnWindowsLooksInEachFolderOnceAndPassesOverWhatIsntAFullPath()
    {
        string steam = Path.Combine(Root, "Steam");
        _user.Set(@"Software\Valve\Steam", "SteamPath", steam);
        _computer.Set(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", "Steam");
        _computer.Set(@"SOFTWARE\Valve\Steam", "InstallPath", 42);

        IReadOnlyList<string> folders = SteamFolders.OnWindows(_user, _computer, Root);

        Assert.Equal([steam], folders);
    }

    [Fact]
    public void OnWindowsAFolderTheRegistryWritesInAnotherCaseIsLookedInOnce()
    {
        // Steam writes the user's SteamPath in lower case, such as c:/program files (x86)/steam; Windows' names ignore case.
        string steam = Path.Combine(Root, "Steam");
        _user.Set(@"Software\Valve\Steam", "SteamPath", Path.Combine(Root, "steam"));
        _computer.Set(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", steam);

        IReadOnlyList<string> folders = SteamFolders.OnWindows(_user, _computer, Root);

        Assert.Equal([Path.Combine(Root, "steam")], folders);
    }

    [Fact]
    public void OnWindowsWithoutSteamInTheRegistryLooksInTheDefaultFolderAlone()
    {
        Assert.Equal([Path.Combine(Root, "Steam")], SteamFolders.OnWindows(_user, _computer, Root));
        Assert.Empty(SteamFolders.OnWindows(_user, _computer, string.Empty));
    }

    [Fact]
    public void OnWindowsAKeyTheUserMayNotReadIsPassedOver()
    {
        Assert.Equal([Path.Combine(Root, "Steam")], SteamFolders.OnWindows(new ClosedRegistryTree(), _computer, Root));
    }

    [Fact]
    public void OnLinuxLooksInFlatpaksThenTheUsersThenSnaps()
    {
        string home = Path.Combine(Root, "home");

        IReadOnlyList<string> folders = SteamFolders.OnLinux(home, dataHome: null);

        Assert.Equal(
            [
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam"),
                Path.Combine(home, "snap", "steam", "common", ".steam", "steam"),
            ],
            folders);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnLinuxTheUsersDataFolderIsWhereXdgSaysWhenItsAFullPath(bool fullPath)
    {
        string home = Path.Combine(Root, "home");
        string data = Path.Combine(Root, "data");

        IReadOnlyList<string> folders = SteamFolders.OnLinux(home, fullPath ? data : "data");

        Assert.Equal(fullPath ? Path.Combine(data, "Steam") : Path.Combine(home, ".local", "share", "Steam"), folders[2]);
    }

    /// <summary>
    /// A registry hive whose keys the user may not read.
    /// </summary>
    private sealed class ClosedRegistryTree : Platform.Links.IRegistryTree
    {
        public bool Exists(string path) => throw new UnauthorizedAccessException();

        public string? GetString(string path, string? name) => throw new UnauthorizedAccessException();

        public void SetString(string path, string? name, string value) => throw new UnauthorizedAccessException();

        public void DeleteValue(string path, string name) => throw new UnauthorizedAccessException();

        public void CopyTree(string from, string to) => throw new UnauthorizedAccessException();

        public void DeleteTree(string path) => throw new UnauthorizedAccessException();

        public void DeleteIfEmpty(string path) => throw new UnauthorizedAccessException();
    }
}
