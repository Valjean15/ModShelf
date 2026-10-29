using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ModShelf.Application.Settings;
using ModShelf.Application.Settings.Ports;
using ModShelf.Domain.Library;
using ModShelf.Domain.Updates;

namespace ModShelf.Infrastructure.Settings;

/// <summary>
/// Keeps the settings in a JSON file the user can read and edit, such as:
/// <code>
/// {
///   "library": {
///     "root": "D:\\Mods",
///     "folders": "{source}/{game}/{mod} ({modid})/{file}",
///     "gameFolders": { "skyrimspecialedition": "{source}/{game}/{mod}/{file}" },
///     "oldVersions": 2
///   },
///   "updates": { "interval": "6h" },
///   "appearance": { "theme": "dark" }
/// }
/// </code>
/// </summary>
/// <remarks>
/// Settings left out take their defaults. Saving keeps whatever else the file holds, such as a newer version's settings,
/// and replaces the file in one step, so it's never left half-written.
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
        try
        {
            string? root = Text(library, Root);
            if (root is not null && !Path.IsPathFullyQualified(root))
            {
                throw new InvalidDataException($"library.root must be a full path, not '{root}'.");
            }

            ImmutableDictionary<string, FolderPattern>.Builder games = ImmutableDictionary.CreateBuilder<string, FolderPattern>(StringComparer.OrdinalIgnoreCase);
            if (library[GameFolders] is JsonObject gameFolders)
            {
                foreach ((string game, JsonNode? pattern) in gameFolders)
                {
                    games[game] = FolderPattern.Parse(pattern?.GetValue<string>() ?? throw new InvalidDataException($"library.gameFolders.{game} is empty."));
                }
            }

            string? folders = Text(library, Folders);
            return new AppSettings
            {
                LibraryRoot = root,
                LibraryFolders = folders is null ? null : FolderPattern.Parse(folders),
                GameFolders = games.ToImmutable(),
                OldVersions = ReadOldVersions(library),
                UpdateInterval = Text(updates, Interval, Updates) is { } interval ? UpdateInterval.Parse(interval) : null,
                NotifyOfUpdates = updates[Notify] switch
                {
                    null => null,
                    JsonValue value when value.TryGetValue(out bool notify) => notify,
                    _ => throw new InvalidDataException($"{Updates}.{Notify} must be true or false."),
                },
                Theme = Text(appearance, Theme, Appearance) is { } theme
                    ? ThemeNamed(theme) ?? throw new InvalidDataException($"{Appearance}.{Theme} must be system, light or dark.")
                    : null,
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
        if (settings.GameFolders.Count == 0)
        {
            library.Remove(GameFolders);
        }
        else
        {
            JsonObject games = [];
            foreach ((string game, FolderPattern pattern) in settings.GameFolders.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                games[game] = pattern.Text;
            }

            library[GameFolders] = games;
        }

        // A count as a number, the others as text: "oldVersions": 2, or "oldVersions": "archive".
        switch (settings.OldVersions)
        {
            case null:
                library.Remove(OldVersions);
                break;
            case { Action: OldVersionAction.KeepLatest } rule:
                library[OldVersions] = rule.Count;
                break;
            case { } rule:
                library[OldVersions] = rule.ToString();
                break;
        }

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
    /// Writes <paramref name="text"/> to a temporary file of its own beside <paramref name="path"/>, then puts it in
    /// place in one step. When that fails, the temporary file goes too.
    /// </summary>
    internal static async Task ReplaceAsync(string path, string text, CancellationToken cancellationToken)
    {
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
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
    /// The rule for old versions: a count, or text as <see cref="OldVersionRule.Parse"/> reads it.
    /// </summary>
    private static OldVersionRule? ReadOldVersions(JsonObject library) => library[OldVersions] switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out int count) => OldVersionRule.Parse(count.ToString(CultureInfo.InvariantCulture)),
        JsonValue value when value.TryGetValue(out string? text) => OldVersionRule.Parse(text),
        _ => throw new InvalidDataException($"library.{OldVersions} must be all, archive, or a number of versions to keep."),
    };

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
