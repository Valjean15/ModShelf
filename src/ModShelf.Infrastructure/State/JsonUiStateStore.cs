using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

using ModShelf.Application.State;
using ModShelf.Application.State.Ports;
using ModShelf.Infrastructure.Settings;

namespace ModShelf.Infrastructure.State;

/// <summary>
/// Keeps the desktop app's state in a JSON file of its own, <c>ui-state.json</c> beside <c>settings.json</c>:
/// <code>
/// {
///   "window": { "x": 120, "y": 80, "width": 1280, "height": 800, "maximized": false },
///   "menuCollapsed": true,
///   "screen": "browse",
///   "browseSearches": [ "skyui", "armour" ],
///   "quickSearches": [ "dragon", "old versions" ]
/// }
/// </code>
/// A file that can't be read is forgotten, and replaced when the state is next saved; a value that isn't what it should
/// be is left out. What else the file holds stays.
/// </summary>
internal sealed partial class JsonUiStateStore : IUiStateStore
{
    private const string Window = "window";
    private const string X = "x";
    private const string Y = "y";
    private const string Width = "width";
    private const string Height = "height";
    private const string Maximized = "maximized";
    private const string MenuCollapsed = "menuCollapsed";
    private const string Screen = "screen";
    private const string BrowseSearches = "browseSearches";
    private const string QuickSearches = "quickSearches";

    private static readonly JsonDocumentOptions Reading = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions Writing = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<JsonUiStateStore> _logger;

    /// <param name="path">The file's absolute path. It's created when the state is first saved.</param>
    public JsonUiStateStore(string path, ILogger<JsonUiStateStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is not an absolute path.", nameof(path));
        }

        _path = path;
        _logger = logger;
    }

    public string Location => _path;

    public async Task<UiState> LoadAsync(CancellationToken cancellationToken)
    {
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is not { } file)
        {
            return UiState.None;
        }

        return new UiState
        {
            Window = file[Window] is JsonObject window ? PlacementOf(window) : null,
            MenuCollapsed = Flag(file[MenuCollapsed]),
            Screen = Text(file[Screen]),
            BrowseSearches = file[BrowseSearches] is JsonArray searches ? [.. searches.Select(Text).OfType<string>()] : [],
            QuickSearches = file[QuickSearches] is JsonArray quick ? [.. quick.Select(Text).OfType<string>()] : [],
        };
    }

    public async Task SaveAsync(UiState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        // What the file holds besides this state stays.
        JsonObject file = await ReadAsync(cancellationToken).ConfigureAwait(false) ?? [];
        if (state.Window is { } placement)
        {
            JsonObject window = file[Window] as JsonObject ?? [];
            window[X] = placement.X;
            window[Y] = placement.Y;
            window[Width] = placement.Width;
            window[Height] = placement.Height;
            window[Maximized] = placement.Maximized;
            file[Window] = window;
        }
        else
        {
            file.Remove(Window);
        }

        Set(file, MenuCollapsed, state.MenuCollapsed is { } collapsed ? JsonValue.Create(collapsed) : null);
        Set(file, Screen, state.Screen is { } screen ? JsonValue.Create(screen) : null);
        Set(file, BrowseSearches, state.BrowseSearches.Count > 0 ? new JsonArray([.. state.BrowseSearches.Select(search => JsonValue.Create(search))]) : null);
        Set(file, QuickSearches, state.QuickSearches.Count > 0 ? new JsonArray([.. state.QuickSearches.Select(search => JsonValue.Create(search))]) : null);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await JsonSettingsStore.ReplaceAsync(_path, file.ToJsonString(Writing) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"The UI state couldn't be saved to {_path}: {exception.Message}", exception);
        }
    }

    private static WindowPlacement? PlacementOf(JsonObject window)
        => Number(window[X]) is { } x && double.IsInteger(x) && x is >= int.MinValue and <= int.MaxValue
            && Number(window[Y]) is { } y && double.IsInteger(y) && y is >= int.MinValue and <= int.MaxValue
            && Number(window[Width]) is { } width && double.IsFinite(width) && width > 0
            && Number(window[Height]) is { } height && double.IsFinite(height) && height > 0
                ? new WindowPlacement((int)x, (int)y, width, height, Flag(window[Maximized]) ?? false)
                : null;

    private static double? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue(out double number) ? number : null;

    private static bool? Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static void Set(JsonObject file, string name, JsonNode? value)
    {
        if (value is null)
        {
            file.Remove(name);
        }
        else
        {
            file[name] = value;
        }
    }

    /// <returns>The file's object; null when there's no file, or it can't be read.</returns>
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
            LogUnreadable(_logger, _path, exception.Message);
            return null;
        }

        try
        {
            if (JsonNode.Parse(text, documentOptions: Reading) is JsonObject file)
            {
                return file;
            }

            LogUnreadable(_logger, _path, "The file must hold a JSON object.");
        }
        catch (JsonException exception)
        {
            LogUnreadable(_logger, _path, exception.Message);
        }

        return null;
    }
}
