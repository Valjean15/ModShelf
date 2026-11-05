using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Games;
using ModShelf.Presentation.ViewModels.Pictures;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.ViewModels.Settings;

/// <summary>
/// Settings' Add a game…: the game picker over the sites' games not listed yet, a game chosen added at once; or a game's
/// key typed, such as skyrimspecialedition, for a game the sites can't list, as without an account.
/// </summary>
public sealed partial class AddGameViewModel : ObservableObject
{
    private readonly IModLookup _lookup;
    private readonly ISourceRegistry _sources;
    private readonly HashSet<GameRef> _listed;
    private readonly ILogger _logger;

    /// <param name="listed">The games listed already, which aren't offered.</param>
    internal AddGameViewModel(IModLookup lookup, ISourceRegistry sources, ModPictures pictures, IEnumerable<GameRef> listed, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(logger);

        _lookup = lookup;
        _sources = sources;
        _listed = [.. listed];
        _logger = logger;
        Picker = new GamePickerViewModel(pictures, sources, Choose, typed: Type);
    }

    /// <summary>
    /// Asks to close the dialog, the game chosen or the key typed added.
    /// </summary>
    public event EventHandler? AddRequested;

    public GamePickerViewModel Picker { get; }

    /// <summary>
    /// Whether the sites' games are being read.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReading { get; private set; }

    /// <summary>
    /// Why a site's games couldn't be read, with how to add one by its key; null when every site's were.
    /// </summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>
    /// The game chosen; null when none was, or a key was typed.
    /// </summary>
    public GameRef? Game { get; private set; }

    /// <summary>
    /// What was typed for a game none listed, to read as a game's key, such as skyrimspecialedition; null when a game
    /// was chosen.
    /// </summary>
    public string? TypedKey { get; private set; }

    /// <summary>
    /// The reading of the sites' games, for tests to wait on.
    /// </summary>
    internal Task Reading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Starts reading the games of every site that can list them, but those listed already, for the picker.
    /// </summary>
    internal void StartReading(CancellationToken cancellationToken) => Reading = ReadAsync(cancellationToken);

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        IsReading = true;
        List<GameInfo> games = [];
        List<string> problems = [];
        try
        {
            foreach (IModCatalog catalog in _sources.All<IModCatalog>())
            {
                try
                {
                    games.AddRange(await _lookup.GetGamesAsync(catalog.Id, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (!SiteProblems.IsSiteProblem(exception))
                    {
                        LogGamesUnreadable(_logger, exception, catalog.Id.Value);
                    }

                    problems.Add(SiteProblems.Describe(exception, catalog.DisplayName, Strings.SettingsAddGameCantList) ?? Strings.SomethingWentWrong);
                }
            }
        }
        finally
        {
            IsReading = false;
        }

        Picker.ShowGames(games.Where(game => !_listed.Contains(game.Game)));
        Problem = problems.Count == 0 ? null : string.Join(' ', [.. problems, Strings.SettingsAddGameByKey]);
    }

    private void Choose(GameRef game)
    {
        Game = game;
        TypedKey = null;
        AddRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Type(string typed)
    {
        Game = null;
        TypedKey = typed;
        AddRequested?.Invoke(this, EventArgs.Empty);
    }
}
