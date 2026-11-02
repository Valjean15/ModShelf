using System.Collections.Immutable;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Pictures;
using ModShelf.Application.Settings;

namespace ModShelf.Presentation.ViewModels.Pictures;

/// <summary>
/// What the screens show mods' pictures with: the previews each screen shows, how adult mods' pictures look, the mods'
/// saved details and the saved pictures. The choices are read from the settings once; one changed through it is saved,
/// and <see cref="Changed"/> tells every screen.
/// </summary>
public sealed partial class ModPictures
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ModPictures> _logger;
    private ImmutableDictionary<PreviewScreen, PreviewLevel> _levels = ImmutableDictionary<PreviewScreen, PreviewLevel>.Empty;
    private Task? _loading;

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
    /// A screen's previews, or how adult mods' pictures look, changed.
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
    /// Reads the choices from the settings, the first time it's called; later calls wait for that read. Settings that
    /// can't be read leave the defaults.
    /// </summary>
    internal Task LoadAsync() => _loading ??= ReadAsync();

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
        try
        {
            await _settings.SetPreviewAsync(screen, level == AppSettings.DefaultPreview(screen) ? null : level, CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            LogPreviewsNotSaved(_logger, screen, exception.Message);
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
            Adult = settings.AdultPictures ?? AdultPictures.Blurred;
        }
        catch (InvalidDataException exception)
        {
            // The Settings screen says so; pictures show as they do by default meanwhile.
            LogSettingsUnreadable(_logger, exception.Message);
        }
    }
}
