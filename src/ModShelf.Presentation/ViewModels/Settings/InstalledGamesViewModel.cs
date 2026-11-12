using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Installed;
using ModShelf.Application.Installed.Ports;
using ModShelf.Application.Sources;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.Services;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Settings › Games' games on this PC: the games Steam installed here, each with its games on the sites, by name or as
/// chosen, and <b>Choose…</b> to choose another, or say a site has none. Read again each time it's shown.
/// </summary>
public sealed partial class InstalledGamesViewModel : ObservableObject
{
    private readonly IInstalledGameMatcher _matcher;
    private readonly IModLookup _lookup;
    private readonly ISourceRegistry _sources;
    private readonly ModPictures _pictures;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private IReadOnlyList<GameInfo> _listed = [];

    // The stores found as last matched; null before.
    private IReadOnlyList<GameStore>? _stores;

    public InstalledGamesViewModel(
        IInstalledGameMatcher matcher,
        IModLookup lookup,
        ISourceRegistry sources,
        ModPictures pictures,
        IDialogService dialogs,
        ILogger<InstalledGamesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(logger);

        _matcher = matcher;
        _lookup = lookup;
        _sources = sources;
        _pictures = pictures;
        _dialogs = dialogs;
        _logger = logger;
    }

    /// <summary>
    /// The installed games, by name.
    /// </summary>
    public ObservableCollection<InstalledGameRowViewModel> Games { get; } = [];

    [ObservableProperty]
    public partial bool HasGames { get; private set; }

    /// <summary>
    /// Whether the installed games, or the sites' games, are being read.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReading { get; private set; }

    /// <summary>
    /// Such as "Steam: 42 games installed, 30 with a site's game.", or that Steam isn't here; null before it's read.
    /// </summary>
    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    /// <summary>
    /// Why a site's games couldn't be read, so its games match only as chosen; null when every site's were.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// The last reading, for tests to wait on.
    /// </summary>
    internal Task Reading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads the installed games and the sites' games again, as Settings › Games shows.
    /// </summary>
    public void OnShown() => Reading = ReadAsync();

    /// <summary>
    /// Matches the installed games at once, by the games listed before, then again as each site that lists its games
    /// answers, every site asked at once: a slow site keeps none of the others waiting. The games, and the problems, stay
    /// in the sites' order, however they answered. A site's games are kept while the app runs.
    /// </summary>
    private async Task ReadAsync()
    {
        IsReading = true;
        List<string> problems = [];
        try
        {
            Show(await _matcher.MatchAsync(_listed, CancellationToken.None));
            IModCatalog[] listing = [.. _sources.All<IModCatalog>().Where(catalog => !_lookup.FindsGamesByName(catalog.Id))];
            IReadOnlyList<GameInfo>[] read = [.. listing.Select(catalog => (IReadOnlyList<GameInfo>)[.. _listed.Where(game => game.Game.Source == catalog.Id)])];
            Task<(IReadOnlyList<GameInfo>? Games, string? Problem)>[] answers = [.. listing.Select(SiteGamesAsync)];
            List<Task<(IReadOnlyList<GameInfo>? Games, string? Problem)>> reading = [.. answers];
            while (reading.Count > 0)
            {
                Task<(IReadOnlyList<GameInfo>? Games, string? Problem)> done = await Task.WhenAny(reading);
                reading.Remove(done);
                int site = Array.IndexOf(answers, done);
                IReadOnlyList<GameInfo> games = (await done).Games ?? [];
                if (games.Count > 0 || read[site].Count > 0)
                {
                    read[site] = games;
                    _listed = [.. read.SelectMany(siteGames => siteGames)];
                    Show(await _matcher.MatchAsync(_listed, CancellationToken.None));
                }
            }

            foreach (Task<(IReadOnlyList<GameInfo>? Games, string? Problem)> answer in answers)
            {
                if ((await answer).Problem is { } problem)
                {
                    problems.Add(problem);
                }
            }
        }
        catch (Exception exception)
        {
            LogInstalledUnread(_logger, exception);
            problems.Add(Strings.SomethingWentWrong);
        }
        finally
        {
            IsReading = false;
        }

        if (_stores is { } stores)
        {
            ShowStatus(stores);
        }

        Problem = problems.Count == 0 ? null : string.Join(' ', problems);
    }

    /// <summary>
    /// Says where the games were found, and how many have a site's game, once the sites have answered.
    /// </summary>
    private void ShowStatus(IReadOnlyList<GameStore> stores)
        => StatusText = stores.Count == 0
            ? Strings.SettingsInstalledNoStore
            : Text.Format(
                Strings.SettingsInstalledFound,
                Text.List(stores.Select(store => store.Name)),
                Counts.Of(Games.Count, Strings.SettingsInstalledOneGame, Strings.SettingsInstalledGames),
                Games.Count(row => row.HasGame));

    /// <summary>
    /// The site's games; or, when it couldn't list them, why.
    /// </summary>
    private async Task<(IReadOnlyList<GameInfo>? Games, string? Problem)> SiteGamesAsync(IModCatalog catalog)
    {
        try
        {
            return (await _lookup.GetGamesAsync(catalog.Id, CancellationToken.None), null);
        }
        catch (Exception exception)
        {
            if (!SiteProblems.IsSiteProblem(exception))
            {
                LogGamesUnread(_logger, exception, catalog.Id.Value);
            }

            return (null, SiteProblems.Describe(exception, catalog.DisplayName, Strings.SettingsInstalledCantList) ?? Strings.SomethingWentWrong);
        }
    }

    private void Show(InstalledGamesMatched matched)
    {
        Dictionary<string, InstalledGameRowViewModel> rows = Games.ToDictionary(row => row.Game.Key, StringComparer.Ordinal);
        List<InstalledGameRowViewModel> shown = [];
        foreach (InstalledGameMatch match in matched.Games)
        {
            if (rows.TryGetValue(match.Game.Key, out InstalledGameRowViewModel? row))
            {
                row.Show(match);
            }
            else
            {
                row = new InstalledGameRowViewModel(match, _sources, ChooseAsync);
            }

            shown.Add(row);
        }

        Games.SyncWith(shown);
        HasGames = Games.Count > 0;
        _stores = matched.Stores;
    }

    /// <summary>
    /// Opens the dialog to choose the installed game's games, then reads the games again.
    /// </summary>
    private async Task ChooseAsync(InstalledGameRowViewModel row)
    {
        InstalledGamePickViewModel picking = new(row.Match, _listed, _matcher, _lookup, _sources, _pictures, _logger);
        await _dialogs.PickInstalledGameAsync(picking, CancellationToken.None);
        await picking.Changing;
        OnShown();
        await Reading;
    }
}
