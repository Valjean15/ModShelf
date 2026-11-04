using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// A game's page before a search: rows of its trending, newest and recently updated mods, and its categories for the
/// category box.
/// </summary>
public sealed partial class BrowseViewModel
{
    private readonly CategoryChoice _allCategories = new(null, Strings.BrowseAllCategories);
    private CancellationTokenSource? _rowsListing;

    // The game and adult content the rows were listed for, so showing them again doesn't list them again; null when
    // they weren't all listed, so showing them again tries again.
    private (GameRef Game, bool Adult)? _rowsListed;

    /// <summary>
    /// The rows a game's page shows before a search: Trending, Recently added and Recently updated.
    /// </summary>
    public IReadOnlyList<BrowseRowViewModel> Rows { get; }

    /// <summary>
    /// Whether the rows show in place of the list: once a game is chosen, until a search; and again when the words
    /// searched for are cleared.
    /// </summary>
    [ObservableProperty]
    public partial bool IsShowingRows { get; private set; }

    /// <summary>
    /// The last listing of the rows, for tests to wait on.
    /// </summary>
    internal Task RowsWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The last reading of the game's categories, for tests to wait on.
    /// </summary>
    internal Task CategoriesWork { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Clearing the words searched for shows the game's rows again, dropping a search under way.
    /// </summary>
    partial void OnSearchTextChanged(string oldValue, string newValue)
    {
        if (!string.IsNullOrWhiteSpace(oldValue)
            && string.IsNullOrWhiteSpace(newValue)
            && Game is { } game
            && (!IsShowingRows || IsBusy)
            && !IsShowingTracked)
        {
            ShowRows(game.Game, closeMod: false);
        }
    }

    /// <summary>
    /// Shows the game's rows in place of the list, dropping a listing under way, and lists them unless they were listed
    /// for it already.
    /// </summary>
    /// <param name="closeMod">Whether the mod opened closes too, as when another game is chosen.</param>
    private void ShowRows(GameRef game, bool closeMod)
    {
        CancellationTokenSource? dropped = Interlocked.Exchange(ref _listing, null);
        try
        {
            dropped?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended meanwhile.
        }

        IsBusy = false;
        Tell(null, needsAccount: false);
        if (closeMod)
        {
            Mod = null;
        }

        Results.Clear();
        _query = null;
        _searched = null;
        _trackedSource = null;
        IsShowingTracked = false;
        HasMore = false;
        ResultsText = string.Empty;
        IsShowingRows = true;
        if (_rowsListed != (game, IncludeAdultContent))
        {
            RowsWork = ListRowsAsync(game);
        }
    }

    /// <summary>
    /// Lists the game's rows afresh, all three at once, dropping a listing of the rows under way. Trending shows where
    /// the game's site lists its trending mods, and the newest and the recently updated where its search has those
    /// orders; adult mods only when the user asked for them.
    /// </summary>
    private async Task ListRowsAsync(GameRef game)
    {
        using CancellationTokenSource listing = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        CancellationTokenSource? dropped = Interlocked.Exchange(ref _rowsListing, listing);
        try
        {
            dropped?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended meanwhile.
        }

        bool adult = IncludeAdultContent;
        _rowsListed = (game, adult);
        string site = _services.Sources.SiteName(game.Source);
        ITrendingMods? trending = _services.Sources.Find<ITrendingMods>(game.Source);
        SearchCapabilities? search = _services.Sources.Find<IModSearch>(game.Source)?.Capabilities;
        Rows[0].IsShown = trending is not null;
        Rows[1].IsShown = search?.Has(new SearchOrder(SearchSort.Newest, SearchDirection.Descending)) ?? true;
        Rows[2].IsShown = search?.Has(new SearchOrder(SearchSort.Updated, SearchDirection.Descending)) ?? true;
        foreach (BrowseRowViewModel row in Rows)
        {
            row.Load();
        }

        int count = Math.Min(RowSize, search?.MaxCount ?? RowSize);
        bool[] listed = await Task.WhenAll(
            ListRowAsync(Rows[0], site, token => trending?.GetTrendingAsync(game, token) ?? Task.FromResult<IReadOnlyList<ModInfo>>([]), BrowseFact.None, adult, listing.Token),
            ListRowAsync(Rows[1], site, token => NewestAsync(Rows[1], game, SearchSort.Newest, count, adult, token), BrowseFact.Published, adult, listing.Token),
            ListRowAsync(Rows[2], site, token => NewestAsync(Rows[2], game, SearchSort.Updated, count, adult, token), BrowseFact.Updated, adult, listing.Token));
        if (Interlocked.CompareExchange(ref _rowsListing, null, listing) == listing && listed.Contains(false))
        {
            _rowsListed = null;
        }
    }

    /// <summary>
    /// The first of the game's mods in the order, as many as a row lists; none for a row not shown.
    /// </summary>
    private async Task<IReadOnlyList<ModInfo>> NewestAsync(BrowseRowViewModel row, GameRef game, SearchSort sort, int count, bool adult, CancellationToken cancellationToken)
        => row.IsShown ? (await _lookup.SearchAsync(game, new SearchQuery(sort: sort, count: count, includeAdultContent: adult), cancellationToken)).Mods : [];

    /// <summary>
    /// Lists a row, or says why it couldn't. A listing a later one replaced changes nothing.
    /// </summary>
    /// <returns>Whether it was listed.</returns>
    private async Task<bool> ListRowAsync(
        BrowseRowViewModel row,
        string site,
        Func<CancellationToken, Task<IReadOnlyList<ModInfo>>> list,
        BrowseFact fact,
        bool adult,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ModInfo> mods = await list(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            row.Show(mods.Where(mod => adult || !mod.HasAdultContent).Take(RowSize).Select(mod => Row(mod, problem: null, fact)));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (Describe(exception, site, Strings.BrowseRowFailed) is { } problem)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                row.Fail(problem);
                if (exception is AuthenticationFailedException)
                {
                    Tell(problem, needsAccount: true);
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                LogRowFailed(_logger, exception, row.Kind.ToString());
                row.Fail(Strings.SomethingWentWrong);
            }

            return false;
        }
    }

    /// <summary>
    /// Lists every mod in a row's order, from the first page, with the words, author and filters in the boxes.
    /// </summary>
    private Task ShowAllAsync(BrowseRowKind kind)
    {
        SearchSort sort = kind == BrowseRowKind.RecentlyAdded ? SearchSort.Newest : SearchSort.Updated;
        _choosingQuietly = true;
        try
        {
            SelectedSort = Sorts.First(choice => choice.Sort == sort && choice.Direction == SearchDirection.Descending);
        }
        finally
        {
            _choosingQuietly = false;
        }

        Listing = SearchListingAsync();
        return Listing;
    }

    /// <summary>
    /// Chooses every category, then reads the game's categories from its site for the category box, by name. When the
    /// site can't say, or can't search by them, the box offers every category only.
    /// </summary>
    private async Task ReadCategoriesAsync(GameRef game)
    {
        _choosingQuietly = true;
        try
        {
            SelectedCategory = _allCategories;
            Categories.SyncWith([_allCategories]);
        }
        finally
        {
            _choosingQuietly = false;
        }

        if (!CanSearchByCategory || _services.Sources.Find<IModCatalog>(game.Source) is not { } catalog)
        {
            return;
        }

        try
        {
            GameInfo info = await catalog.GetGameAsync(game, _stopping);
            if (Game?.Game == game)
            {
                StringComparer byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
                Categories.SyncWith([_allCategories, .. info.Categories.Order(byName).Select(category => new CategoryChoice(category, category))]);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The app is closing.
        }
        catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
        {
            // Expected without an account, or offline, which a search says.
        }
        catch (Exception exception)
        {
            LogCategoriesFailed(_logger, exception, game.Key, game.Source.Value);
        }
    }
}
