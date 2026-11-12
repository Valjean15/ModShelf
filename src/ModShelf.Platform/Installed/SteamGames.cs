using System.Globalization;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Installed.Ports;

namespace ModShelf.Platform.Installed;

/// <summary>
/// The games the Steam client installed on this computer, from its own files: the first of its possible folders that
/// has a <c>steamapps</c> folder is Steam's; <c>steamapps/libraryfolders.vdf</c> there names its other library folders;
/// and each library's <c>steamapps/appmanifest_&lt;app ID&gt;.acf</c> names a game, its app ID and its folder under
/// <c>steamapps/common</c>. A game still downloading, and Steam's own tools (Proton, its runtimes and redistributables),
/// are left out. Nothing is written, and nothing is sent anywhere.
/// </summary>
internal sealed partial class SteamGames : IInstalledGames
{
    internal const string StoreId = "steam";

    internal const string StoreName = "Steam";

    // StateFlags: the game is fully installed, though it may also be updating.
    private const int FullyInstalled = 4;

    // Proton's versions and runtimes ("Proton 9.0", "Proton Experimental", "Proton EasyAntiCheat Runtime"), Steam's Linux
    // runtimes, and the redistributables games install from.
    private static readonly string[] ToolNames = ["Proton ", "Steam Linux Runtime", "Steamworks Common Redistributables"];

    private readonly Func<IReadOnlyList<string>> _folders;
    private readonly ILogger _logger;

    /// <param name="folders">Where Steam may be, in the order to look; read each time the games are.</param>
    public SteamGames(Func<IReadOnlyList<string>> folders, ILogger<SteamGames> logger)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(logger);

        _folders = folders;
        _logger = logger;
    }

    public Task<InstalledGames> ReadAsync(CancellationToken cancellationToken)
        => Task.Run(() => Read(cancellationToken), cancellationToken);

    private static bool IsTool(string name)
        => ToolNames.Any(tool => name.StartsWith(tool, StringComparison.OrdinalIgnoreCase));

    private InstalledGames Read(CancellationToken cancellationToken)
    {
        string? steam = _folders().FirstOrDefault(folder => Directory.Exists(Path.Combine(folder, "steamapps")));
        if (steam is null)
        {
            LogNotFound(_logger);
            return InstalledGames.None;
        }

        Dictionary<string, InstalledGame> games = new(StringComparer.Ordinal);
        int libraries = 0;
        foreach (string library in Libraries(steam))
        {
            cancellationToken.ThrowIfCancellationRequested();
            libraries++;
            foreach (InstalledGame game in GamesIn(library, cancellationToken))
            {
                games.TryAdd(game.Id, game);
            }
        }

        LogRead(_logger, games.Count, libraries);
        StringComparer byName = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);
        return new InstalledGames([new GameStore(StoreId, StoreName, steam)], [.. games.Values.OrderBy(game => game.Name, byName)]);
    }

    /// <summary>
    /// Steam's own folder, then the other library folders its list names, each once.
    /// </summary>
    private List<string> Libraries(string steam)
    {
        // A folder written in another case is the same folder where names ignore case, as on Windows.
        StringComparer same = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        List<string> libraries = [steam];
        string list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(list))
        {
            return libraries;
        }

        KeyValue? folders;
        try
        {
            folders = KeyValue.Parse(File.ReadAllText(list))["libraryfolders"];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogLibrariesUnread(_logger, exception);
            return libraries;
        }

        foreach (KeyValue folder in folders?.Children ?? [])
        {
            // Since 2021 each folder is a group with its path; before, the path itself.
            string? path = folder.Value ?? folder.Text("path");
            if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && !libraries.Contains(Path.GetFullPath(path), same))
            {
                libraries.Add(Path.GetFullPath(path));
            }
        }

        return libraries;
    }

    private List<InstalledGame> GamesIn(string library, CancellationToken cancellationToken)
    {
        string apps = Path.Combine(library, "steamapps");
        List<InstalledGame> games = [];
        string[] manifests;
        try
        {
            manifests = Directory.Exists(apps) ? Directory.GetFiles(apps, "appmanifest_*.acf") : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogLibraryUnread(_logger, exception);
            return games;
        }

        foreach (string manifest in manifests.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Game(apps, manifest) is { } game)
            {
                games.Add(game);
            }
        }

        return games;
    }

    /// <summary>
    /// The game a manifest names, when it's installed and not one of Steam's tools; null otherwise, or when the manifest
    /// can't be read, which is logged.
    /// </summary>
    private InstalledGame? Game(string apps, string manifest)
    {
        string file = Path.GetFileNameWithoutExtension(manifest);
        string named = file["appmanifest_".Length..];
        KeyValue? state;
        try
        {
            state = KeyValue.Parse(File.ReadAllText(manifest))["AppState"];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LogManifestUnread(_logger, named, exception);
            return null;
        }

        string? id = state?.Text("appid") ?? named;
        string? name = state?.Text("name")?.Trim();
        string? folder = state?.Text("installdir");
        if (!IsAppId(id) || string.IsNullOrEmpty(name) || !IsFolderName(folder))
        {
            LogManifestIncomplete(_logger, named);
            return null;
        }

        if (state!.Text("StateFlags") is { } flags
            && int.TryParse(flags, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && (value & FullyInstalled) == 0)
        {
            LogNotInstalled(_logger, id);
            return null;
        }

        return IsTool(name) ? null : new InstalledGame(StoreId, id, name, Path.Combine(apps, "common", folder));
    }

    private static bool IsAppId(string? id)
        => !string.IsNullOrEmpty(id) && id.All(char.IsAsciiDigit);

    /// <summary>
    /// Whether a manifest's folder is one folder's name, not a path that could lead elsewhere.
    /// </summary>
    private static bool IsFolderName([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? folder)
        => !string.IsNullOrWhiteSpace(folder)
            && folder is not ("." or "..")
            && folder.IndexOfAny(['/', '\\']) < 0
            && folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
