using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Keeps mods' details through the <see cref="IModDetailsRepository"/>, read from their site's <see cref="IModCatalog"/>
/// when they're missing or old. One read of a mod serves every caller asking meanwhile, and at most
/// <see cref="SiteReadsAtOnce"/> reads run at once, so a library opening doesn't ask a site about all its mods together.
/// </summary>
internal sealed partial class ModDetails : IModDetails, IDisposable
{
    /// <summary>
    /// How many mods' details are read from the sites at once.
    /// </summary>
    internal const int SiteReadsAtOnce = 4;

    private readonly ISourceRegistry _sources;
    private readonly IModDetailsRepository _repository;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<ModDetails> _logger;
    private readonly ConcurrentDictionary<ModRef, Lazy<Task<ModInfo?>>> _reads = new();
    private readonly SemaphoreSlim _reading = new(SiteReadsAtOnce, SiteReadsAtOnce);

    public ModDetails(
        ISourceRegistry sources,
        IModDetailsRepository repository,
        ISettingsService settings,
        TimeProvider time,
        ILogger<ModDetails> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _repository = repository;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    public async Task<ModInfo?> GetSavedAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        return (await FindAsync(modRef, cancellationToken).ConfigureAwait(false))?.Details;
    }

    public async Task<IReadOnlyDictionary<ModRef, ModInfo>> GetSavedAsync(IReadOnlyCollection<ModRef> mods, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mods);

        if (mods.Count == 0)
        {
            return new Dictionary<ModRef, ModInfo>();
        }

        IReadOnlyDictionary<ModRef, SavedModDetails> saved = await _repository.FindAsync(mods, cancellationToken).ConfigureAwait(false);
        return saved.ToDictionary(pair => pair.Key, pair => pair.Value.Details);
    }

    public async Task<ModInfo?> GetAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        SavedModDetails? saved = await FindAsync(modRef, cancellationToken).ConfigureAwait(false);
        if (saved is not null && await IsFreshAsync(saved, cancellationToken).ConfigureAwait(false))
        {
            return saved.Details;
        }

        if (_sources.Find<IModCatalog>(modRef.Game.Source) is not { } catalog)
        {
            return saved?.Details;
        }

        // Read for whoever asks first and those who ask meanwhile, so one caller giving up doesn't stop the others.
        Lazy<Task<ModInfo?>> reading = _reads.GetOrAdd(modRef, key => new Lazy<Task<ModInfo?>>(() => ReadAsync(catalog, key)));
        return await reading.Value.WaitAsync(cancellationToken).ConfigureAwait(false) ?? saved?.Details;
    }

    public void Dispose() => _reading.Dispose();

    private async Task<SavedModDetails?> FindAsync(ModRef mod, CancellationToken cancellationToken)
    {
        try
        {
            return await _repository.FindAsync(mod, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            // As if none were saved: the site's replace them.
            LogSavedUnreadable(_logger, mod.Id, mod.Game.Key, mod.Game.Source.Value, exception.Message);
            return null;
        }
    }

    private async Task<bool> IsFreshAsync(SavedModDetails saved, CancellationToken cancellationToken)
    {
        TimeSpan maxAge;
        try
        {
            maxAge = (await _settings.GetAsync(cancellationToken).ConfigureAwait(false)).DetailsMaxAge ?? AppSettings.DefaultDetailsMaxAge;
        }
        catch (InvalidDataException)
        {
            maxAge = AppSettings.DefaultDetailsMaxAge;
        }

        // Saved in the future, by a clock that was set back since, counts as old.
        TimeSpan age = _time.GetUtcNow() - saved.SavedAt;
        return age >= TimeSpan.Zero && age < maxAge;
    }

    /// <returns>The site's details, saved; null when the site couldn't say.</returns>
    private async Task<ModInfo?> ReadAsync(IModCatalog catalog, ModRef mod)
    {
        try
        {
            ModInfo details;
            await _reading.WaitAsync().ConfigureAwait(false);
            try
            {
                details = await catalog.GetModAsync(mod, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or AuthenticationFailedException or OperationCanceledException or ArgumentException)
            {
                // Offline, refused, timed out, or a game or mod the site doesn't know: asked again next time.
                LogCantRead(_logger, mod.Id, mod.Game.Key, mod.Game.Source.Value, exception.Message);
                return null;
            }
            finally
            {
                _reading.Release();
            }

            await _repository.SaveAsync(new SavedModDetails(details, _time.GetUtcNow()), CancellationToken.None).ConfigureAwait(false);
            return details;
        }
        finally
        {
            _reads.TryRemove(mod, out _);
        }
    }
}
