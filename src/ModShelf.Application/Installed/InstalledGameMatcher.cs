using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Common;
using ModShelf.Application.Installed.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Installed;

/// <summary>
/// Matches the installed games as <see cref="IInstalledGames"/> reads them each time, with the games picked for them in
/// the settings. The games picked that the sites had to be asked for are kept while the app runs.
/// </summary>
internal sealed partial class InstalledGameMatcher : IInstalledGameMatcher
{
    private readonly IInstalledGames _installed;
    private readonly SettingsService _settings;
    private readonly ISourceRegistry _sources;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<GameRef, GameInfo> _asked = new(AppSettings.GameComparer);

    public InstalledGameMatcher(IInstalledGames installed, SettingsService settings, ISourceRegistry sources, ILogger<InstalledGameMatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);

        _installed = installed;
        _settings = settings;
        _sources = sources;
        _logger = logger;
    }

    public async Task<InstalledGamesMatched> MatchAsync(IEnumerable<GameInfo> listed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listed);

        GameInfo[] games = [.. listed.DistinctBy(game => game.Game)];
        InstalledGames installed = await _installed.ReadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, IReadOnlyDictionary<SourceId, string?>> picks = await PicksAsync(cancellationToken).ConfigureAwait(false);
        SourceId[] sites = [.. games.Select(game => game.Game.Source).Distinct()];
        List<InstalledGameMatch> matched = [];
        foreach (InstalledGame game in installed.Games)
        {
            IReadOnlyDictionary<SourceId, string?> picked = picks.GetValueOrDefault(game.Key) ?? new Dictionary<SourceId, string?>();
            List<SiteMatch> on = [];
            foreach (SourceId site in sites.Concat(picked.Keys.Where(site => !sites.Contains(site)).OrderBy(site => site.Value, StringComparer.Ordinal)))
            {
                on.Add(picked.TryGetValue(site, out string? key)
                    ? new SiteMatch(site, key is null ? [] : [await PickedAsync(new GameRef(site, key), games, cancellationToken).ConfigureAwait(false)], Picked: true)
                    : new SiteMatch(site, [.. games.Where(listing => listing.Game.Source == site && NameQuery.SameName(listing.Name, game.Name))], Picked: false));
            }

            matched.Add(new InstalledGameMatch(game, on));
        }

        int found = matched.Count(match => match.Sites.Any(site => site.Games.Count > 0));
        LogMatched(_logger, matched.Count, found);
        return new InstalledGamesMatched(installed.Stores, matched);
    }

    public Task PickAsync(InstalledGame game, SourceId site, GameRef? picked, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(site);
        if (picked is not null && picked.Source != site)
        {
            throw new ArgumentException($"{picked} isn't a game of {site}.", nameof(picked));
        }

        return _settings.SetInstalledMatchAsync(game.Key, site, picked?.Key, cancellationToken);
    }

    public Task UnpickAsync(InstalledGame game, SourceId site, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(site);

        return _settings.ForgetInstalledMatchAsync(game.Key, site, cancellationToken);
    }

    /// <summary>
    /// The games picked, as the settings keep them; none when the settings can't be read, which is logged, so the games
    /// still match by name.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<SourceId, string?>>> PicksAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await _settings.GetAsync(cancellationToken).ConfigureAwait(false)).InstalledMatches;
        }
        catch (InvalidDataException exception)
        {
            LogPicksUnread(_logger, exception);
            return new Dictionary<string, IReadOnlyDictionary<SourceId, string?>>();
        }
    }

    /// <summary>
    /// What's known of a game picked: as listed, as its site said before, or as its site says now; its key for its name
    /// when the site can't say, which is logged.
    /// </summary>
    private async Task<GameInfo> PickedAsync(GameRef game, GameInfo[] listed, CancellationToken cancellationToken)
    {
        if (listed.FirstOrDefault(listing => AppSettings.GameComparer.Equals(listing.Game, game)) is { } shown)
        {
            return shown;
        }

        if (_asked.TryGetValue(game, out GameInfo? known))
        {
            return known;
        }

        if (_sources.Find<IModCatalog>(game.Source) is { } catalog)
        {
            try
            {
                return _asked.GetOrAdd(game, await catalog.GetGameAsync(game, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPickedUnread(_logger, game.Source.Value, exception);
            }
        }

        return new GameInfo(game, game.Key);
    }
}
