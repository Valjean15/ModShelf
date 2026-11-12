using ModShelf.Application.Installed.Ports;
using ModShelf.Platform.Installed;
using ModShelf.Testing;

namespace ModShelf.Platform.Tests.Installed;

/// <summary>
/// Steam's installed games, read from a Steam folder and library folders made in the test, as the Steam client writes
/// them.
/// </summary>
public sealed class SteamGamesTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly RecordingLogger<SteamGames> _logger = new();

    public SteamGamesTests()
    {
        Steam = Path.Combine(_folder.Path, "Steam");
        Library = Path.Combine(_folder.Path, "Games", "SteamLibrary");
    }

    private string Steam { get; }

    private string Library { get; }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ReadsTheGamesOfEachLibraryFolderByName()
    {
        LibraryFolders(Steam, Library);
        Manifest(Steam, "413150", "Stardew Valley");
        Manifest(Library, "489830", "The Elder Scrolls V: Skyrim Special Edition", folder: "Skyrim Special Edition");
        Manifest(Library, "1091500", "Cyberpunk 2077");

        InstalledGames read = await Read(Path.Combine(_folder.Path, "Nowhere"), Steam).ReadAsync(Cancellation);

        Assert.Equal([new GameStore("steam", "Steam", Steam)], read.Stores);
        Assert.Equal(
            [
                new InstalledGame("steam", "1091500", "Cyberpunk 2077", Path.Combine(Library, "steamapps", "common", "Cyberpunk 2077")),
                new InstalledGame("steam", "413150", "Stardew Valley", Path.Combine(Steam, "steamapps", "common", "Stardew Valley")),
                new InstalledGame("steam", "489830", "The Elder Scrolls V: Skyrim Special Edition", Path.Combine(Library, "steamapps", "common", "Skyrim Special Edition")),
            ],
            read.Games);
        Assert.Equal("steam/489830", read.Games[2].Key);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Steam has 3 games installed in 2 library folders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsTheLibraryFoldersListAsSteamWroteItBefore2021()
    {
        File.WriteAllText(
            Path.Combine(Folder(Steam, "steamapps"), "libraryfolders.vdf"),
            $"\"LibraryFolders\"\n{{\n\t\"TimeNextStatsReport\"\t\"1600000000\"\n\t\"1\"\t\"{Escaped(Library)}\"\n}}\n");
        Manifest(Library, "1091500", "Cyberpunk 2077");

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        Assert.Equal(["Cyberpunk 2077"], read.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task ReadsAGameOnceThoughTwoLibrariesNameIt()
    {
        LibraryFolders(Steam, Steam, Library, Library);
        Manifest(Steam, "413150", "Stardew Valley");
        Manifest(Library, "413150", "Stardew Valley", folder: "Elsewhere");

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        InstalledGame game = Assert.Single(read.Games);
        Assert.Equal(Path.Combine(Steam, "steamapps", "common", "Stardew Valley"), game.Folder);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Steam has 1 games installed in 2 library folders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LeavesOutGamesStillDownloadingButNotThoseUpdating()
    {
        Manifest(Steam, "1", "Downloading", flags: "1026");
        Manifest(Steam, "2", "Updating", flags: "6");
        Manifest(Steam, "3", "Installed", flags: "4");
        Manifest(Steam, "4", "No flags", flags: null);

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        Assert.Equal(["Installed", "No flags", "Updating"], read.Games.Select(game => game.Name));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Debug: Steam app 1 isn't fully installed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LeavesOutSteamsOwnTools()
    {
        Manifest(Steam, "228980", "Steamworks Common Redistributables");
        Manifest(Steam, "1493710", "Proton Experimental");
        Manifest(Steam, "2805730", "Proton 9.0");
        Manifest(Steam, "1628350", "Steam Linux Runtime 3.0 (sniper)");
        Manifest(Steam, "5", "Protonaut");

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        Assert.Equal(["Protonaut"], read.Games.Select(game => game.Name));
    }

    [Theory]
    [InlineData("\"AppState\" { \"appid\" \"7\" \"name\" ", "Couldn't read Steam's manifest of app 7")]
    [InlineData("\"AppState\" { \"appid\" \"7\" \"installdir\" \"Game\" }", "Steam's manifest of app 7 doesn't say")]
    [InlineData("\"AppState\" { \"appid\" \"7\" \"name\" \"Game\" }", "Steam's manifest of app 7 doesn't say")]
    [InlineData("\"AppState\" { \"appid\" \"x7\" \"name\" \"Game\" \"installdir\" \"Game\" }", "Steam's manifest of app 7 doesn't say")]
    [InlineData("\"AppState\" { \"appid\" \"7\" \"name\" \"Game\" \"installdir\" \"../../Elsewhere\" }", "Steam's manifest of app 7 doesn't say")]
    [InlineData("\"AppState\" { \"appid\" \"7\" \"name\" \"Game\" \"installdir\" \"..\" }", "Steam's manifest of app 7 doesn't say")]
    [InlineData("\"AppState\" { \"appid\" \"7\" \"name\" \"Game\" \"installdir\" \"..\\\\Elsewhere\" }", "Steam's manifest of app 7 doesn't say")]
    [InlineData("\"AppState\" { \"appid\" \"7\" \"name\" \"  \" \"installdir\" \"Game\" }", "Steam's manifest of app 7 doesn't say")]
    public async Task PassesOverAManifestItCantUseAndReadsTheOthers(string manifest, string logged)
    {
        File.WriteAllText(Path.Combine(Folder(Steam, "steamapps"), "appmanifest_7.acf"), manifest);
        Manifest(Steam, "8", "Readable");

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        Assert.Equal(["Readable"], read.Games.Select(game => game.Name));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: " + logged, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TakesTheAppIdFromTheFileNameWhenTheManifestDoesntSayIt()
    {
        File.WriteAllText(Path.Combine(Folder(Steam, "steamapps"), "appmanifest_70.acf"), "\"AppState\" { \"name\" \"Game\" \"installdir\" \"Game\" }");

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        Assert.Equal("70", Assert.Single(read.Games).Id);
    }

    [Fact]
    public async Task AListOfLibrariesThatCantBeReadLeavesSteamsOwnFolder()
    {
        File.WriteAllText(Path.Combine(Folder(Steam, "steamapps"), "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" {");
        Manifest(Steam, "413150", "Stardew Valley");

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        Assert.Equal(["Stardew Valley"], read.Games.Select(game => game.Name));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Warning: Couldn't read Steam's list of library folders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALibraryThatsGoneOrNotAFullPathIsPassedOver()
    {
        File.WriteAllText(
            Path.Combine(Folder(Steam, "steamapps"), "libraryfolders.vdf"),
            $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{Escaped(Path.Combine(_folder.Path, "Gone"))}\" }} \"2\" {{ \"path\" \"SteamLibrary\" }} \"3\" {{ \"label\" \"\" }} }}");
        Manifest(Steam, "413150", "Stardew Valley");

        InstalledGames read = await Read(Steam).ReadAsync(Cancellation);

        Assert.Equal(["Stardew Valley"], read.Games.Select(game => game.Name));
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Steam has 1 games installed in 2 library folders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutSteamThereAreNoGames()
    {
        Directory.CreateDirectory(Path.Combine(_folder.Path, "NotSteam"));

        InstalledGames read = await Read(Path.Combine(_folder.Path, "NotSteam"), Path.Combine(_folder.Path, "Nowhere")).ReadAsync(Cancellation);

        Assert.Same(InstalledGames.None, read);
        Assert.Contains(_logger.Entries, entry => entry.StartsWith("Information: Steam isn't on this computer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LooksForSteamEachTimeAndLogsNoPath()
    {
        int looked = 0;
        SteamGames games = new(() =>
        {
            looked++;
            return [Steam];
        }, _logger);
        Manifest(Steam, "413150", "Stardew Valley");

        await games.ReadAsync(Cancellation);
        await games.ReadAsync(Cancellation);

        Assert.Equal(2, looked);
        Assert.DoesNotContain(_logger.Entries, entry => entry.Contains(_folder.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopsWhenAsked()
    {
        Manifest(Steam, "413150", "Stardew Valley");
        using CancellationTokenSource stopped = new();
        await stopped.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(Steam).ReadAsync(stopped.Token));
    }

    private static string Folder(string library, string name) => Directory.CreateDirectory(Path.Combine(library, name)).FullName;

    private static string Escaped(string path) => path.Replace(@"\", @"\\", StringComparison.Ordinal);

    private static void LibraryFolders(string steam, params string[] libraries)
    {
        string entries = string.Concat(libraries.Select((library, index) => $"\t\"{index}\"\n\t{{\n\t\t\"path\"\t\t\"{Escaped(library)}\"\n\t\t\"apps\"\n\t\t{{\n\t\t}}\n\t}}\n"));
        File.WriteAllText(Path.Combine(Folder(steam, "steamapps"), "libraryfolders.vdf"), $"\"libraryfolders\"\n{{\n{entries}}}\n");
    }

    private static void Manifest(string library, string id, string name, string? folder = null, string? flags = "4")
    {
        string state = flags is null ? string.Empty : $"\t\"StateFlags\"\t\t\"{flags}\"\n";
        File.WriteAllText(
            Path.Combine(Folder(library, "steamapps"), $"appmanifest_{id}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{id}\"\n\t\"Universe\"\t\t\"1\"\n\t\"name\"\t\t\"{name}\"\n{state}\t\"installdir\"\t\t\"{folder ?? name}\"\n}}\n");
    }

    private SteamGames Read(params string[] folders) => new(() => folders, _logger);
}
