using System.Collections.Immutable;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Pictures;
using ModShelf.Application.Settings;
using ModShelf.Domain.Mods;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// What the screens show mods' pictures with: the previews each screen shows, and each game that has its own, how adult
/// mods' pictures look, the mods' saved details and the saved pictures. The choices are read from the settings once; one
/// changed through it is saved, and <see cref="Changed"/> tells every screen. The Settings screen shows it the games'
/// previews as it reads them.
/// </summary>
public sealed partial class ModPictures
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ModPictures> _logger;
    private ImmutableDictionary<PreviewScreen, PreviewLevel> _levels = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty;
    private ImmutableDictionary<GameRef, PreviewLevel> _gameLevels = ImmutableDictionary.Create<GameRef, PreviewLevel>(AppSettings.GameComparer);
    private Task? _loading;
    private Task _saving = Task.CompletedTask;

    public ModPictures(ISettingsService settings, IModDetails details, IPictureCache cache, ILogger<ModPictures> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
        Details = details;
        Cache = cache;
    }

    /// <summary>
    /// A screen's previews, a game's, or how adult mods' pictures look, changed.
    /// </summary>
    internal event EventHandler? Changed;

    internal IModDetails Details { get; }

    internal IPictureCache Cache { get; }

    internal ILogger Logger => _logger;

    /// <summary>
    /// How adult mods' pictures look.
    /// </summary>
    internal AdultPictures Adult { get; private set; } = AdultPictures.Blurred;

    /// <summary>
    /// The previews the screen shows: the settings', or its default.
    /// </summary>
    internal PreviewLevel LevelOf(PreviewScreen screen)
        => _levels.TryGetValue(screen, out PreviewLevel level) ? level : AppSettings.DefaultPreview(screen);

    /// <summary>
    /// The previews the screen shows the game's mods with: the game's own on the screens that show one game's mods, the
    /// Library and Browse, when it has some; otherwise the screen's.
    /// </summary>
    /// <param name="game">The game the screen shows; null for none.</param>
    internal PreviewLevel LevelOf(PreviewScreen screen, GameRef? game)
        => OwnLevelOf(screen, game) ?? LevelOf(screen);

    /// <summary>
    /// The game's own previews, on a screen that shows them; null when it has none, or the screen goes by its own.
    /// </summary>
    internal PreviewLevel? OwnLevelOf(PreviewScreen screen, GameRef? game)
        => game is not null && screen is PreviewScreen.Library or PreviewScreen.Browse && _gameLevels.TryGetValue(game, out PreviewLevel own)
            ? own
            : null;

    /// <summary>
    /// Reads the choices from the settings, the first time it's called; later calls wait for that read. Settings that
    /// can't be read leave the defaults.
    /// </summary>
    internal Task LoadAsync() => _loading ??= ReadAsync();

    /// <summary>
    /// Waits for the previews chosen so far to be saved, or not; it never throws.
    /// </summary>
    internal Task SavedAsync() => _saving;

    /// <summary>
    /// Shows the previews on the screen from now on, and saves them. When they can't be saved, they stay while the app
    /// runs.
    /// </summary>
    internal async Task SetLevelAsync(PreviewScreen screen, PreviewLevel level)
    {
        if (LevelOf(screen) == level)
        {
            return;
        }

        _levels = _levels.SetItem(screen, level);
        Changed?.Invoke(this, EventArgs.Empty);
        await Saving(SaveAsync(screen, level));
    }

    /// <summary>
    /// Shows the previews on the screen, for the game it shows, from now on, and saves them: the game's own, when it has
    /// some there, or else the screen's.
    /// </summary>
    /// <param name="game">The game the screen shows; null for none.</param>
    internal async Task SetLevelAsync(PreviewScreen screen, GameRef? game, PreviewLevel level)
    {
        if (game is null || OwnLevelOf(screen, game) is not { } own)
        {
            await SetLevelAsync(screen, level);
            return;
        }

        if (own == level)
        {
            return;
        }

        _gameLevels = _gameLevels.SetItem(game, level);
        Changed?.Invoke(this, EventArgs.Empty);
        await Saving(SaveAsync(game, level));
    }

    /// <summary>
    /// Shows the games' own previews as <paramref name="settings"/> stores them, telling the screens when they changed.
    /// </summary>
    internal void ShowGames(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ImmutableDictionary<GameRef, PreviewLevel> levels = GameLevelsOf(settings);
        if (levels.Count != _gameLevels.Count
            || levels.Any(stored => !_gameLevels.TryGetValue(stored.Key, out PreviewLevel shown) || shown != stored.Value))
        {
            _gameLevels = levels;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Saves how adult mods' pictures look, then shows them so.
    /// </summary>
    /// <exception cref="InvalidDataException">The settings can't be read.</exception>
    /// <exception cref="IOException">The settings can't be saved.</exception>
    internal async Task SetAdultAsync(AdultPictures adult, CancellationToken cancellationToken)
    {
        await _settings.SetAdultPicturesAsync(adult == AdultPictures.Blurred ? null : adult, cancellationToken);
        if (Adult != adult)
        {
            Adult = adult;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ReadAsync()
    {
        try
        {
            AppSettings settings = await _settings.GetAsync(CancellationToken.None);
            _levels = settings.Previews.ToImmutableDictionary();
            _gameLevels = GameLevelsOf(settings);
            Adult = settings.AdultPictures ?? AdultPictures.Blurred;
        }
        catch (InvalidDataException exception)
        {
            // The Settings screen says so; pictures show as they do by default meanwhile.
            LogSettingsUnreadable(_logger, exception.Message);
        }
    }

    private static ImmutableDictionary<GameRef, PreviewLevel> GameLevelsOf(AppSettings settings)
        => settings.Games
            .Where(game => game.Value.Previews is not null)
            .ToImmutableDictionary(game => game.Key, game => game.Value.Previews.GetValueOrDefault(), AppSettings.GameComparer);

    /// <summary>
    /// Keeps a save, for <see cref="SavedAsync"/> to wait for.
    /// </summary>
    private Task Saving(Task save)
    {
        _saving = Task.WhenAll(_saving, save);
        return save;
    }

    private async Task SaveAsync(PreviewScreen screen, PreviewLevel level)
    {
        try
        {
            await _settings.SetPreviewAsync(screen, level == AppSettings.DefaultPreview(screen) ? null : level, CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            LogPreviewsNotSaved(_logger, screen, exception.Message);
        }
    }

    private async Task SaveAsync(GameRef game, PreviewLevel level)
    {
        try
        {
            await _settings.SetGameAsync(game, own => own with { Previews = level }, CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            LogGamePreviewsNotSaved(_logger, game.Source.Value, game.Key, exception.Message);
        }
    }
}
