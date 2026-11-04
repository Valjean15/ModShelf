using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Domain.Updates;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Infrastructure.Settings;

/// <summary>
/// Keeps the settings in a JSON file the user can read and edit, such as:
/// <code>
/// {
///   "library": {
///     "root": "D:\\Mods",
///     "folders": "{source}/{game}/{mod} ({modid})/{file}",
///     "oldVersions": 2
///   },
///   "games": {
///     "nexus/skyrimspecialedition": {
///       "folders": "{source}/{game}/{mod}/{file}",
///       "oldVersions": "archive",
///       "checkUpdates": true,
///       "updateInterval": "1d",
///       "notify": false,
///       "browse": { "sort": "updated", "direction": "descending", "window": "7d", "pageSize": 50 },
///       "previews": "thumbnails",
///       "hidden": true
///     }
///   },
///   "updates": { "interval": "6h" },
///   "appearance": { "theme": "dark" },
///   "window": { "closeToTray": true },
///   "setup": { "done": true },
///   "app": { "checkForUpdates": false },
///   "previews": { "library": "cards", "updates": "thumbnails" },
///   "pictures": { "maxSize": 1024, "adult": "hidden" },
///   "details": { "maxAge": "7d" }
/// }
/// </code>
/// </summary>
/// <remarks>
/// Settings left out take their defaults. Saving keeps whatever else the file holds, such as a newer version's settings,
/// and replaces the file in one step, so it's never left half-written. A game is named by its site and its key there. A
/// game named by its key alone, as before 1.0.0, when only Nexus Mods had games, is Nexus Mods' game, and is named by
/// its site when the settings are next saved. Games' folders in <c>library.gameFolders</c>, as 0.9.2 kept them, are read
/// too, and move to <c>games</c> when the settings are next saved.
/// </remarks>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private const string Library = "library";
    private const string Root = "root";
    private const string Folders = "folders";
    private const string GameFolders = "gameFolders";
    private const string OldVersions = "oldVersions";
    private const string Updates = "updates";
    private const string Interval = "interval";
    private const string Notify = "notify";
    private const string Appearance = "appearance";
    private const string Theme = "theme";
    private const string Window = "window";
    private const string CloseToTray = "closeToTray";
    private const string Setup = "setup";
    private const string Done = "done";
    private const string App = "app";
    private const string CheckForUpdates = "checkForUpdates";
    private const string Previews = "previews";
    private const string Pictures = "pictures";
    private const string MaxSize = "maxSize";
    private const string Adult = "adult";
    private const string Details = "details";
    private const string MaxAge = "maxAge";
    private const string Games = "games";
    private const string CheckUpdates = "checkUpdates";
    private const string UpdateIntervalName = "updateInterval";
    private const string Browse = "browse";
    private const string Sort = "sort";
    private const string Direction = "direction";
    private const string TimeWindow = "window";
    private const string PageSize = "pageSize";
    private const string Hidden = "hidden";

    // Before 1.0.0, a game's settings named the game alone, and only Nexus Mods had games.
    private static readonly SourceId SiteBeforeSites = new("nexus");

    private static readonly JsonDocumentOptions Reading = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions Writing = new() { WriteIndented = true };

    private readonly string _path;

    /// <param name="path">The file's absolute path. It's created when settings are first saved.</param>
    public JsonSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is not an absolute path.", nameof(path));
        }

        _path = path;
    }

    public string Location => _path;

    /// <exception cref="InvalidDataException">
    /// The file can't be opened, isn't JSON, or holds a setting no value can have.
    /// </exception>
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        JsonObject? file = await ReadAsync(cancellationToken).ConfigureAwait(false);
        JsonObject library = file?[Library] as JsonObject ?? [];
        JsonObject updates = file?[Updates] as JsonObject ?? [];
        JsonObject appearance = file?[Appearance] as JsonObject ?? [];
        JsonObject window = file?[Window] as JsonObject ?? [];
        JsonObject setup = file?[Setup] as JsonObject ?? [];
        JsonObject app = file?[App] as JsonObject ?? [];
        JsonObject previews = file?[Previews] as JsonObject ?? [];
        JsonObject pictures = file?[Pictures] as JsonObject ?? [];
        JsonObject details = file?[Details] as JsonObject ?? [];
        try
        {
            string? root = Text(library, Root);
            if (root is not null && !Path.IsPathFullyQualified(root))
            {
                throw new InvalidDataException($"library.root must be a full path, not '{root}'.");
            }

            string? folders = Text(library, Folders);
            return new AppSettings
            {
                LibraryRoot = root,
                LibraryFolders = folders is null ? null : FolderPattern.Parse(folders),
                Games = ReadGames(file?[Games], library[GameFolders]),
                OldVersions = ReadOldVersions(library),
                UpdateInterval = Text(updates, Interval, Updates) is { } interval ? UpdateInterval.Parse(interval) : null,
                NotifyOfUpdates = Flag(updates, Notify, Updates),
                CloseToTray = Flag(window, CloseToTray, Window),
                SetupDone = Flag(setup, Done, Setup),
                CheckForAppUpdates = Flag(app, CheckForUpdates, App),
                Theme = Text(appearance, Theme, Appearance) is { } theme
                    ? ThemeNamed(theme) ?? throw new InvalidDataException($"{Appearance}.{Theme} must be system, light or dark.")
                    : null,
                Previews = ReadPreviews(previews),
                PicturesMegabytes = ReadMegabytes(pictures),
                AdultPictures = Text(pictures, Adult, Pictures) is { } adult
                    ? Named<AdultPictures>(adult) ?? throw new InvalidDataException($"{Pictures}.{Adult} must be hidden, blurred or shown.")
                    : null,
                DetailsMaxAge = Text(details, MaxAge, Details) is { } age ? UpdateInterval.Parse(age) : null,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or FormatException)
        {
            throw new InvalidDataException($"The settings in {_path} can't be read: {exception.Message} Fix the file, or delete it to start again.", exception);
        }
    }

    /// <exception cref="InvalidDataException">The file there can't be read.</exception>
    /// <exception cref="IOException">The file can't be written.</exception>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // What the file holds besides these settings stays.
        JsonObject file = await ReadAsync(cancellationToken).ConfigureAwait(false) ?? [];
        JsonObject library = file[Library] as JsonObject ?? [];
        file[Library] = library;

        Set(library, Root, settings.LibraryRoot);
        Set(library, Folders, settings.LibraryFolders?.Text);

        // Games' folders moved to their own settings.
        library.Remove(GameFolders);
        WriteGames(file, settings.Games);

        WriteOldVersions(library, settings.OldVersions);

        if (library.Count == 0)
        {
            file.Remove(Library);
        }

        JsonObject updatesSection = file[Updates] as JsonObject ?? [];
        Set(updatesSection, Interval, settings.UpdateInterval is { } every ? UpdateInterval.Format(every) : null);
        if (settings.NotifyOfUpdates is { } notify)
        {
            updatesSection[Notify] = notify;
        }
        else
        {
            updatesSection.Remove(Notify);
        }

        if (updatesSection.Count == 0)
        {
            file.Remove(Updates);
        }
        else
        {
            file[Updates] = updatesSection;
        }

        JsonObject windowSection = file[Window] as JsonObject ?? [];
        if (settings.CloseToTray is { } closeToTray)
        {
            windowSection[CloseToTray] = closeToTray;
        }
        else
        {
            windowSection.Remove(CloseToTray);
        }

        if (windowSection.Count == 0)
        {
            file.Remove(Window);
        }
        else
        {
            file[Window] = windowSection;
        }

        JsonObject setupSection = file[Setup] as JsonObject ?? [];
        if (settings.SetupDone is { } done)
        {
            setupSection[Done] = done;
        }
        else
        {
            setupSection.Remove(Done);
        }

        if (setupSection.Count == 0)
        {
            file.Remove(Setup);
        }
        else
        {
            file[Setup] = setupSection;
        }

        JsonObject appSection = file[App] as JsonObject ?? [];
        if (settings.CheckForAppUpdates is { } check)
        {
            appSection[CheckForUpdates] = check;
        }
        else
        {
            appSection.Remove(CheckForUpdates);
        }

        if (appSection.Count == 0)
        {
            file.Remove(App);
        }
        else
        {
            file[App] = appSection;
        }

        // Each screen's previews by name; a screen this version doesn't know stays as it is.
        JsonObject previewsSection = file[Previews] as JsonObject ?? [];
        foreach (PreviewScreen screen in Enum.GetValues<PreviewScreen>())
        {
            Set(previewsSection, Name(screen), settings.Previews.TryGetValue(screen, out PreviewLevel level) ? Name(level) : null);
        }

        Keep(file, Previews, previewsSection);

        JsonObject picturesSection = file[Pictures] as JsonObject ?? [];
        if (settings.PicturesMegabytes is { } megabytes)
        {
            picturesSection[MaxSize] = megabytes;
        }
        else
        {
            picturesSection.Remove(MaxSize);
        }

        Set(picturesSection, Adult, settings.AdultPictures is { } adultPictures ? Name(adultPictures) : null);
        Keep(file, Pictures, picturesSection);

        JsonObject detailsSection = file[Details] as JsonObject ?? [];
        Set(detailsSection, MaxAge, settings.DetailsMaxAge is { } maxAge ? UpdateInterval.Format(maxAge) : null);
        Keep(file, Details, detailsSection);

        JsonObject appearanceSection = file[Appearance] as JsonObject ?? [];
        Set(appearanceSection, Theme, settings.Theme is { } theme ? ThemeName(theme) : null);
        if (appearanceSection.Count == 0)
        {
            file.Remove(Appearance);
        }
        else
        {
            file[Appearance] = appearanceSection;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await ReplaceAsync(_path, file.ToJsonString(Writing) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"The settings couldn't be saved to {_path}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to a temporary file of its own beside <paramref name="path"/>, on to the disk,
    /// then puts it in place in one step, so a crash or a power cut leaves the old settings or the new ones, never an
    /// empty file. When that fails, the temporary file goes too.
    /// </summary>
    internal static async Task ReplaceAsync(string path, string text, CancellationToken cancellationToken)
    {
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using (file.ConfigureAwait(false))
            {
                await file.WriteAsync(new UTF8Encoding(false).GetBytes(text), cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // What went wrong first says more.
            }

            throw;
        }
    }

    private static string? Text(JsonObject section, string name, string sectionName = Library) => section[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out string? text) => string.IsNullOrWhiteSpace(text) ? null : text,
        _ => throw new InvalidDataException($"{sectionName}.{name} must be text."),
    };

    /// <summary>
    /// A setting that's true or false; null when it's left out.
    /// </summary>
    private static bool? Flag(JsonObject section, string name, string sectionName) => section[name] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out bool flag) => flag,
        _ => throw new InvalidDataException($"{sectionName}.{name} must be true or false."),
    };

    /// <summary>
    /// A theme as the file names it: system, light or dark.
    /// </summary>
    private static string ThemeName(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "system",
    };

    /// <returns>The theme named, in any case; null for a name that's none.</returns>
    private static AppTheme? ThemeNamed(string name)
        => Enum.GetValues<AppTheme>().Cast<AppTheme?>().FirstOrDefault(theme => string.Equals(ThemeName(theme!.Value), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A value of an enum as the file names it: in lower case, such as thumbnails.
    /// </summary>
    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    /// <returns>The value named, in any case; null for a name that's none.</returns>
    private static T? Named<T>(string name) where T : struct, Enum
        => Enum.GetValues<T>().Cast<T?>().FirstOrDefault(value => string.Equals(Name(value!.Value), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Each screen's previews; a screen this version doesn't know is left out.
    /// </summary>
    private static ImmutableDictionary<PreviewScreen, PreviewLevel> ReadPreviews(JsonObject previews)
    {
        ImmutableDictionary<PreviewScreen, PreviewLevel>.Builder levels = ImmutableDictionary.CreateBuilder<PreviewScreen, PreviewLevel>();
        foreach ((string name, _) in previews)
        {
            if (Named<PreviewScreen>(name) is { } screen && Text(previews, name, Previews) is { } level)
            {
                levels[screen] = Named<PreviewLevel>(level)
                    ?? throw new InvalidDataException($"{Previews}.{name} must be off, thumbnails or cards.");
            }
        }

        return levels.ToImmutable();
    }

    /// <summary>
    /// The room saved pictures take, a whole number of megabytes within the limits.
    /// </summary>
    private static int? ReadMegabytes(JsonObject pictures) => pictures[MaxSize] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out int megabytes)
            && megabytes is >= AppSettings.MinPicturesMegabytes and <= AppSettings.MaxPicturesMegabytes => megabytes,
        _ => throw new InvalidDataException(
            $"{Pictures}.{MaxSize} must be a number of megabytes from {AppSettings.MinPicturesMegabytes} to {AppSettings.MaxPicturesMegabytes}."),
    };

    /// <summary>
    /// Puts a section in the file, or takes it out when it's empty.
    /// </summary>
    private static void Keep(JsonObject file, string name, JsonObject section)
    {
        if (section.Count == 0)
        {
            file.Remove(name);
        }
        else
        {
            file[name] = section;
        }
    }

    /// <summary>
    /// The rule for old versions: a count, or text as <see cref="OldVersionRule.Parse"/> reads it.
    /// </summary>
    private static OldVersionRule? ReadOldVersions(JsonObject section, string sectionName = Library) => section[OldVersions] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out int count) => OldVersionRule.Parse(count.ToString(CultureInfo.InvariantCulture)),
        JsonValue value when value.TryGetValue(out string? text) => OldVersionRule.Parse(text),
        _ => throw new InvalidDataException($"{sectionName}.{OldVersions} must be all, archive, or a number of versions to keep."),
    };

    /// <summary>
    /// Writes a rule for old versions as a count, or as text: "oldVersions": 2, or "oldVersions": "archive".
    /// </summary>
    private static void WriteOldVersions(JsonObject section, OldVersionRule? rule)
    {
        switch (rule)
        {
            case null:
                section.Remove(OldVersions);
                break;
            case { Action: OldVersionAction.KeepLatest }:
                section[OldVersions] = rule.Count;
                break;
            default:
                section[OldVersions] = rule.ToString();
                break;
        }
    }

    /// <summary>
    /// Each game's own settings, from <c>games</c>, and the folders 0.9.2 kept in <c>library.gameFolders</c>, which a
    /// game's own folders in <c>games</c> come before. A game named by its site comes before the same game named by its key
    /// alone.
    /// </summary>
    private static ImmutableDictionary<GameRef, GameSettings> ReadGames(JsonNode? section, JsonNode? oldFolders)
    {
        ImmutableDictionary<GameRef, GameSettings>.Builder games = ImmutableDictionary.CreateBuilder<GameRef, GameSettings>(AppSettings.GameComparer);
        switch (oldFolders)
        {
            case null:
                break;
            case JsonObject folders:
                foreach ((string game, JsonNode? pattern) in folders)
                {
                    string name = $"{Library}.{GameFolders}.{game}";
                    string text = pattern is JsonValue value && value.TryGetValue(out string? set) && !string.IsNullOrWhiteSpace(set)
                        ? set
                        : throw new InvalidDataException($"{name} must be a folder pattern.");
                    games[GameNamed(game, name)] = new GameSettings { Folders = FolderPattern.Parse(text) };
                }

                break;
            default:
                throw new InvalidDataException($"{Library}.{GameFolders} must hold a folder pattern for each game.");
        }

        switch (section)
        {
            case null:
                break;
            case JsonObject read:
                foreach ((string key, JsonNode? node) in read.OrderBy(entry => entry.Key.Contains('/', StringComparison.Ordinal)))
                {
                    string name = $"{Games}.{key}";
                    GameRef game = GameNamed(key, name);
                    if (node is not JsonObject settings)
                    {
                        throw new InvalidDataException($"{name} must hold the game's settings.");
                    }

                    GameSettings before = games.TryGetValue(game, out GameSettings? old) ? old : GameSettings.None;
                    string? folders = Text(settings, Folders, name);
                    GameSettings own = new()
                    {
                        Folders = folders is null ? before.Folders : FolderPattern.Parse(folders),
                        OldVersions = ReadOldVersions(settings, name),
                        CheckUpdates = Flag(settings, CheckUpdates, name),
                        UpdateInterval = Text(settings, UpdateIntervalName, name) is { } interval ? UpdateInterval.Parse(interval) : null,
                        Notify = Flag(settings, Notify, name),
                        Browse = ReadBrowse(settings[Browse], $"{name}.{Browse}"),
                        Previews = Text(settings, Previews, name) is { } previews
                            ? Named<PreviewLevel>(previews) ?? throw new InvalidDataException($"{name}.{Previews} must be off, thumbnails or cards.")
                            : null,
                        Hidden = Flag(settings, Hidden, name),
                    };
                    // Under the key the game has here, in place of its old folders' own.
                    if (!own.IsNone)
                    {
                        games[game] = own;
                    }
                }

                break;
            default:
                throw new InvalidDataException($"{Games} must hold each game's settings.");
        }

        return games.ToImmutable();
    }

    /// <summary>
    /// The game a key of <c>games</c> names: its site and its key there, such as <c>nexus/stardewvalley</c>, or its key
    /// alone, as before 1.0.0, for Nexus Mods' game.
    /// </summary>
    /// <param name="name">Where the key is, to say so when it names no game.</param>
    private static GameRef GameNamed(string key, string name)
    {
        if (GameRef.TryParse(key, out GameRef? game))
        {
            return game;
        }

        if (!key.Contains('/', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(key))
        {
            return new GameRef(SiteBeforeSites, key.Trim());
        }

        throw new InvalidDataException($"{name} must name a game by its site and its key there, such as nexus/stardewvalley.");
    }

    /// <summary>
    /// The game a key of <c>games</c> names, as <see cref="GameNamed"/> reads it; null for one that names none.
    /// </summary>
    private static GameRef? GameNamedOrNull(string key)
    {
        try
        {
            return GameNamed(key, key);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// How Browse shows a game's mods: an order by name, a direction, a window such as 7d, and a page size.
    /// </summary>
    private static BrowseDefaults? ReadBrowse(JsonNode? node, string name)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject browse:
                BrowseDefaults defaults = new()
                {
                    Sort = Text(browse, Sort, name) is { } sort
                        ? Named<SearchSort>(sort) ?? throw new InvalidDataException($"{name}.{Sort} must be one of {string.Join(", ", Enum.GetValues<SearchSort>().Select(Name))}.")
                        : null,
                    Direction = Text(browse, Direction, name) is { } direction
                        ? Named<SearchDirection>(direction) ?? throw new InvalidDataException($"{name}.{Direction} must be ascending or descending.")
                        : null,
                    UpdatedWithin = Text(browse, TimeWindow, name) is { } window
                        ? WindowNamed(window) ?? throw new InvalidDataException($"{name}.{TimeWindow} must be 1d, 7d or 30d.")
                        : null,
                    PageSize = browse[PageSize] switch
                    {
                        null => null,
                        JsonValue value when value.TryGetValue(out int size) && BrowseDefaults.PageSizes.Contains(size) => size,
                        _ => throw new InvalidDataException($"{name}.{PageSize} must be 20, 50 or 100."),
                    },
                };
                return defaults.IsNone ? null : defaults;
            default:
                throw new InvalidDataException($"{name} must hold how Browse shows the game's mods.");
        }
    }

    /// <returns>The window written, such as 7d, when Browse offers it; null otherwise.</returns>
    private static TimeSpan? WindowNamed(string text)
    {
        foreach (TimeSpan window in BrowseDefaults.Windows)
        {
            if (string.Equals(WindowText(window), text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return null;
    }

    private static string WindowText(TimeSpan window) => $"{window.TotalDays.ToString(CultureInfo.InvariantCulture)}d";

    /// <summary>
    /// Writes each game's own settings to <c>games</c>, by site and key, in the games' order, keeping in each game's what
    /// this version doesn't know; a game with no setting left goes, unless it holds such settings. A game named by its key
    /// alone is named by its site from now on.
    /// </summary>
    private static void WriteGames(JsonObject file, IReadOnlyDictionary<GameRef, GameSettings> games)
    {
        JsonObject before = file[Games] as JsonObject ?? [];
        JsonObject section = [];

        // What the file holds of each game: named by its site first, so what that holds comes before the key alone's.
        Dictionary<GameRef, JsonObject> kept = new(AppSettings.GameComparer);
        foreach ((string key, JsonNode? node) in before.OrderByDescending(entry => entry.Key.Contains('/', StringComparison.Ordinal)))
        {
            if (GameNamedOrNull(key) is not { } named)
            {
                section[key] = node?.DeepClone();
            }
            else if (node is JsonObject held && !kept.ContainsKey(named))
            {
                kept[named] = (JsonObject)held.DeepClone();
            }
        }

        IEnumerable<GameRef> all = kept.Keys
            .Concat(games.Keys)
            .Distinct(AppSettings.GameComparer)
            .OrderBy(game => game.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (GameRef stored in all)
        {
            JsonObject game = kept.TryGetValue(stored, out JsonObject? held) ? held : [];
            GameRef key = games.Keys.FirstOrDefault(set => AppSettings.GameComparer.Equals(set, stored)) ?? stored;
            GameSettings settings = games.TryGetValue(key, out GameSettings? set) ? set : GameSettings.None;
            Set(game, Folders, settings.Folders?.Text);
            WriteOldVersions(game, settings.OldVersions);
            SetFlag(game, CheckUpdates, settings.CheckUpdates);
            Set(game, UpdateIntervalName, settings.UpdateInterval is { } interval ? UpdateInterval.Format(interval) : null);
            SetFlag(game, Notify, settings.Notify);
            WriteBrowse(game, settings.Browse);
            Set(game, Previews, settings.Previews is { } previews ? Name(previews) : null);
            SetFlag(game, Hidden, settings.Hidden);
            if (game.Count > 0)
            {
                section[key.ToString()] = game;
            }
        }

        Keep(file, Games, section);
    }

    private static void WriteBrowse(JsonObject game, BrowseDefaults? defaults)
    {
        JsonObject browse = game[Browse] as JsonObject ?? [];
        Set(browse, Sort, defaults?.Sort is { } sort ? Name(sort) : null);
        Set(browse, Direction, defaults?.Direction is { } direction ? Name(direction) : null);
        Set(browse, TimeWindow, defaults?.UpdatedWithin is { } window ? WindowText(window) : null);
        if (defaults?.PageSize is { } size)
        {
            browse[PageSize] = size;
        }
        else
        {
            browse.Remove(PageSize);
        }

        Keep(game, Browse, browse);
    }

    private static void SetFlag(JsonObject section, string name, bool? value)
    {
        if (value is { } flag)
        {
            section[name] = flag;
        }
        else
        {
            section.Remove(name);
        }
    }

    private static void Set(JsonObject section, string name, string? value)
    {
        if (value is null)
        {
            section.Remove(name);
        }
        else
        {
            section[name] = value;
        }
    }

    /// <returns>The file's object, or null when there's no file yet.</returns>
    /// <exception cref="InvalidDataException">The file can't be opened, or isn't a JSON object.</exception>
    private async Task<JsonObject?> ReadAsync(CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Such as a file another program has locked, or a folder in its place.
            throw new InvalidDataException($"The settings in {_path} can't be read: {exception.Message}", exception);
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: Reading) as JsonObject
                ?? throw new InvalidDataException($"The settings in {_path} can't be read: the file must hold a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The settings in {_path} can't be read: {exception.Message} Fix the file, or delete it to start again.", exception);
        }
    }
}
