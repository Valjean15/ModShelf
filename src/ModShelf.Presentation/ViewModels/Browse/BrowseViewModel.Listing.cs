using System.Net;

using CommunityToolkit.Mvvm.Input;

using ModShelf.Application.Catalog;
using ModShelf.Application.Downloads;
using ModShelf.Application.State;
using ModShelf.Domain.Mods;
using ModShelf.Presentation.Common;
using ModShelf.Presentation.Resources;
using ModShelf.Presentation.ViewModels.Downloads;
using ModShelf.Presentation.ViewModels.ModPages;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;

namespace ModShelf.Presentation.ViewModels.Browse;

/// <summary>
/// Listing mods, by a search or as the account tracks them, and opening one, telling the user what went wrong.
/// </summary>
public sealed partial class BrowseViewModel
{
    /// <summary>
    /// The text as a link to open, when it's a complete one, such as https://… or nxm://…; null for words to search.
    /// </summary>
    private static Uri? AsLink(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? link) ? link : null;
    }

    /// <param name="failed">What to say of a refusal other than the account's, such as "{0} couldn't search: {1}".</param>
    private static string? Describe(Exception exception, string site, string failed)
        => exception is ArgumentException refused ? refused.MessageForUser() : SiteProblems.Describe(exception, site, failed);

    /// <summary>
    /// Searches the chosen game's mods for the words in the box, from the first page; or opens the mod the box's link is
    /// to. A search started meanwhile is dropped.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync()
    {
        Listing = AsLink(SearchText) is { } link ? OpenListingAsync(token => _lookup.LookUpAsync(link, token), source: null) : SearchListingAsync();
        return Listing;
    }

    /// <summary>
    /// Lists the next page: of the search's results, or of the tracked mods.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync()
    {
        if (IsShowingTracked && _trackedSource is { } source)
        {
            Listing = ListingAsync(source, FollowedFailed(source), token => ShowTrackedPageAsync(source, token));
        }
        else if (_query is { } query && _searched is { } game)
        {
            // The same search, from the same moment for the time window, so the pages follow on.
            SearchQuery next = new(
                query.Text, query.Sort, _nextOffset, query.Count, query.Author, query.IncludeAdultContent, query.Direction, query.Category, query.UpdatedSince);
            Listing = ListingAsync(game.Source, Strings.BrowseSearchFailed, token => ShowSearchAsync(game, next, token));
        }

        return Listing;
    }

    private bool CanLoadMore() => HasMore && !IsBusy;

    /// <summary>
    /// Lists the mods of the chosen game the account tracks on its site, read afresh, the highest IDs first: on sites
    /// that number their mods, the newest.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowTracked))]
    private Task ShowTrackedAsync()
    {
        if (Game is not { } game)
        {
            return Task.CompletedTask;
        }

        SourceId source = game.Game.Source;
        string followed = _services.Sources.Words(source).Followed ?? string.Empty;
        Listing = ListingAsync(
            source,
            FollowedFailed(source),
            async token =>
            {
                IReadOnlyList<ModRef> tracked = await _services.Tracked.ReadAsync(source, token);
                token.ThrowIfCancellationRequested();
                _query = null;
                _searched = null;
                _trackedSource = source;
                _trackedToShow =
                [
                    .. tracked
                        .Where(mod => mod.Game == game.Game)
                        .OrderByDescending(mod => mod.Id.Length)
                        .ThenByDescending(mod => mod.Id, StringComparer.Ordinal),
                ];
                Mod = null;
                Results.Clear();
                IsShowingRows = false;
                IsShowingTracked = true;
                OrderText = string.Empty;
                string site = _services.Sources.SiteName(source);
                ResultsText = _trackedToShow.Count switch
                {
                    0 => Text.Format(Strings.BrowseFollowedNone, followed, game.Name, site),
                    1 => Text.Format(Strings.BrowseFollowedOne, followed, game.Name, site),
                    int count => Text.Format(Strings.BrowseFollowed, count, followed, game.Name, site),
                };
                HasMore = false;
                await ShowTrackedPageAsync(source, token);
            });
        return Listing;
    }

    private bool CanShowTracked() => Game is { } game && _services.Tracked.CanTrack(game.Game.Source);

    /// <summary>
    /// Such as "{0} couldn't list your tracked mods: {1}", in the site's words.
    /// </summary>
    private string FollowedFailed(SourceId source)
        => SiteProblems.InWords(Strings.BrowseFollowedFailed, _services.Sources.Words(source).Followed ?? string.Empty);

    /// <summary>
    /// Searches the chosen game again with the order, filters and page size asked for, when the list holds a search; or
    /// searches it in place of its rows.
    /// </summary>
    private void SearchAgain()
    {
        if (!_choosingQuietly && Game is not null && (_query is not null || IsShowingRows))
        {
            Listing = SearchListingAsync();
        }
    }

    /// <summary>
    /// Searches the chosen game's mods for the box's words, from the first page. A link in the box is left out.
    /// </summary>
    private Task SearchListingAsync()
    {
        if (Game is not { } game)
        {
            Tell(Strings.BrowseChooseGame, needsAccount: false);
            return Task.CompletedTask;
        }

        // Only what the site can search by, as the toolbar shows: an author typed for another site's game is kept for it.
        string? words = AsLink(SearchText) is null ? SearchText : null;
        if (!string.IsNullOrWhiteSpace(words))
        {
            RememberingSearch = RememberSearchAsync(words);
        }

        DateTimeOffset? since = SelectedTimeWindow.Within is { } within && CanSearchByUpdated ? _services.TimeProvider.GetUtcNow() - within : null;
        SearchQuery query = new(
            words,
            SelectedSort.Sort,
            0,
            SelectedPageSize.Count,
            CanSearchByAuthor ? Author : null,
            IncludeAdultContent,
            SelectedSort.Direction,
            CanSearchByCategory ? SelectedCategory?.Category : null,
            since);
        return ListingAsync(game.Game.Source, Strings.BrowseSearchFailed, token => ShowSearchAsync(game.Game, query, token));
    }

    /// <summary>
    /// Searches a recent search again in the chosen game, as choosing it in the game picker does.
    /// </summary>
    private void SearchAgainFor(string text)
    {
        SearchText = text;
        Listing = SearchListingAsync();
    }

    /// <summary>
    /// Remembers the words as Browse's newest search, for the game picker to offer. One that can't be remembered is
    /// logged, and the search goes on.
    /// </summary>
    private async Task RememberSearchAsync(string words)
    {
        try
        {
            await _uiState.ChangeAsync(state => state.WithBrowseSearch(words), _stopping);
            UiState remembered = await _uiState.GetAsync(_stopping);
            _uiThread.Post(() => Picker.ShowRecent(remembered.BrowseSearches));
        }
        catch (IOException exception)
        {
            LogSearchNotRemembered(_logger, exception);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The app is closing.
        }
    }

    /// <summary>
    /// Lists a page of a search's results: the first in place of the list, or the next after those listed, leaving out
    /// any listed already, as when the site's results moved between pages. The first page closes the mod opened.
    /// </summary>
    private async Task ShowSearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        SearchPage page = await _lookup.SearchAsync(game, query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Offset == 0)
        {
            Results.Clear();
            IsShowingRows = false;
            IsShowingTracked = false;
            _trackedSource = null;
            Mod = null;
            _query = query;
            _searched = game;
            OrderText = Sorts.FirstOrDefault(choice => choice.Sort == query.Sort && choice.Direction == query.Direction) is { } order
                ? Text.Format(Strings.BrowseOrderNote, order.Name)
                : string.Empty;
        }

        HashSet<ModRef> listed = [.. Results.Select(row => row.Mod)];
        BrowseFact fact = query.Sort switch
        {
            SearchSort.Size => BrowseFact.Size,
            SearchSort.Newest => BrowseFact.Published,
            SearchSort.Updated => BrowseFact.Updated,
            _ => BrowseFact.None,
        };
        foreach (ModInfo mod in page.Mods.Where(mod => listed.Add(mod.Mod)))
        {
            Results.Add(Row(mod, problem: null, fact));
        }

        _nextOffset = page.NextOffset;
        HasMore = page.HasMore;
        ResultsText = Counts.Of(page.TotalCount, Strings.BrowseNoMatches, Strings.BrowseOneMatch, Strings.BrowseMatches);
    }

    /// <summary>
    /// Lists the next page of the tracked mods, with their details from their site, a few at a time. A mod whose details
    /// can't be read is listed by its ID, saying why.
    /// </summary>
    private async Task ShowTrackedPageAsync(SourceId source, CancellationToken cancellationToken)
    {
        ModRef[] page = [.. _trackedToShow.Skip(Results.Count).Take(SelectedPageSize.Count)];
        (ModInfo Mod, string? Problem)[] read = new (ModInfo, string?)[page.Length];
        IModCatalog? catalog = _services.Sources.Find<IModCatalog>(source);
        await Parallel.ForEachAsync(
            Enumerable.Range(0, page.Length),
            new ParallelOptions { MaxDegreeOfParallelism = ReadsAtOnce, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                ModRef mod = page[index];
                ModInfo byId = new(mod, Text.Format(Strings.BrowseModNumbered, mod.Id));
                try
                {
                    read[index] = catalog is null ? (byId, null) : (await catalog.GetModAsync(mod, token), null);
                }
                catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
                {
                    read[index] = (byId, Text.Format(Strings.BrowseModUnreadable, exception.Message));
                }
            });
        cancellationToken.ThrowIfCancellationRequested();

        foreach ((ModInfo mod, string? problem) in read)
        {
            Results.Add(Row(mod, problem));
        }

        HasMore = Results.Count < _trackedToShow.Count;
    }

    private BrowseModViewModel Row(ModInfo mod, string? problem, BrowseFact fact = BrowseFact.None)
    {
        BrowseModViewModel row = new(
            mod,
            problem,
            _inLibrary.GetValueOrDefault(mod.Mod),
            _pictures,
            _services.Sources.Words(mod.Mod.Game.Source),
            _services.TimeProvider,
            OpenAsync,
            ShowSelection,
            fact);
        if (_picturesShown)
        {
            row.Picture.Show(mod, _stopping);
        }

        return row;
    }

    /// <summary>
    /// Opens a mod of the list, read afresh from its site.
    /// </summary>
    private Task OpenAsync(ModRef mod, CancellationToken cancellationToken)
    {
        Listing = OpenListingAsync(token => _lookup.LookUpAsync(mod, token), mod.Game.Source);
        return Listing;
    }

    /// <param name="source">The mod's site, when known; null for a link's.</param>
    private Task OpenListingAsync(Func<CancellationToken, Task<ModPage>> lookUp, SourceId? source)
        => ListingAsync(
            source,
            Strings.BrowseOpenFailed,
            Strings.BrowseNoSuchMod,
            async token =>
            {
                ModPage page = await lookUp(token);
                token.ThrowIfCancellationRequested();
                ModPageViewModel opened = new(page, mod => _inLibrary.GetValueOrDefault(mod), _services, Previews, OpenAsync, _stopping)
                {
                    ShowSite = await _sitesInUse.AreSeveralAsync(token),
                };
                Mod = opened;
                await opened.LoadAsync(token);
                await opened.ShowTrackedAsync(token);
            });

    /// <summary>
    /// Runs one listing at a time: one started later drops this one. What the site refuses is told the user.
    /// </summary>
    /// <param name="source">The site asked, to name in what's told; null for a link's, which isn't known.</param>
    /// <param name="failed">What to say when the site refuses, such as "{0} couldn't search: {1}".</param>
    private Task ListingAsync(SourceId? source, string failed, Func<CancellationToken, Task> list)
        => ListingAsync(source, failed, notFound: null, list);

    /// <param name="notFound">What to say when the site has no such thing (404); null to say it as any refusal.</param>
    /// <inheritdoc cref="ListingAsync(SourceId?, string, Func{CancellationToken, Task})"/>
    private async Task ListingAsync(SourceId? source, string failed, string? notFound, Func<CancellationToken, Task> list)
    {
        using CancellationTokenSource listing = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
        CancellationTokenSource? dropped = Interlocked.Exchange(ref _listing, listing);
        try
        {
            dropped?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended meanwhile.
        }

        IsBusy = true;
        Tell(null, needsAccount: false);
        try
        {
            await list(listing.Token);
        }
        catch (OperationCanceledException) when (listing.IsCancellationRequested)
        {
            // Another listing replaced this one, or the app is closing.
        }
        catch (Exception exception) when (Explain(exception, source, failed, notFound) is { } problem)
        {
            Tell(problem, exception is AuthenticationFailedException);
        }
        catch (Exception exception)
        {
            LogListingFailed(_logger, exception);
            Tell(Strings.SomethingWentWrong, needsAccount: false);
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _listing, null, listing) == listing)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// What to tell the user of a site's refusal: a link's as the Downloads screen tells it, the rest naming the site.
    /// </summary>
    private string? Explain(Exception exception, SourceId? source, string failed, string? notFound)
    {
        if (source is not { } site)
        {
            return exception is UnsupportedLinkException ? Strings.BrowseUnknownLink : LinkProblems.Describe(exception, library: false);
        }

        if (notFound is not null && exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone })
        {
            return notFound;
        }

        return Describe(exception, _services.Sources.SiteName(site), failed);
    }

    /// <param name="problem">What went wrong; null for nothing.</param>
    /// <param name="games">Whether it's that the games couldn't be read.</param>
    private void Tell(string? problem, bool needsAccount, bool games = false)
    {
        Problem = problem;
        NeedsAccount = needsAccount;
        IsGamesProblem = games;
    }
}
