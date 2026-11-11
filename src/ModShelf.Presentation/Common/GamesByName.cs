using Microsoft.Extensions.Logging;

using ModShelf.Application.Catalog;
using ModShelf.Application.Common;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Common;

/// <summary>
/// The games of the sites that find theirs by name, as GameBanana does, found as a name is typed: for the lists that
/// find games, while the other sites' games are read whole.
/// </summary>
internal sealed partial class GamesByName
{
    private readonly IModLookup _lookup;
    private readonly ILogger _logger;
    private readonly Func<GameRef, bool> _leftOut;

    /// <param name="sources">The sites the list finds games of; those among them that find theirs by name are asked.</param>
    /// <param name="leftOut">Which games the list doesn't offer, such as those listed already; null for none.</param>
    public GamesByName(IModLookup lookup, IEnumerable<SourceId> sources, ILogger logger, Func<GameRef, bool>? leftOut = null)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);

        _lookup = lookup;
        _logger = logger;
        _leftOut = leftOut ?? (_ => false);
        Sites = [.. sources.Where(lookup.FindsGamesByName)];
    }

    /// <summary>
    /// The sites asked, in order.
    /// </summary>
    public IReadOnlyList<SourceId> Sites { get; }

    /// <summary>
    /// Whether <paramref name="source"/> is one of the sites whose games are found by name.
    /// </summary>
    public bool Finds(SourceId source) => Sites.Contains(source);

    /// <summary>
    /// The games of every site asked whose names match what was typed, each site's that start with it first. A site that
    /// can't search by what was typed, such as a word too short, finds none; so does one that can't be reached or refuses,
    /// which is logged.
    /// </summary>
    /// <param name="only">The one site to ask, when it's among them; null for all of them.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> stopped it, as when more is typed.</exception>
    public async Task<IReadOnlyList<GameInfo>> FindAsync(string typed, SourceId? only, CancellationToken cancellationToken)
    {
        NameQuery query = NameQuery.Parse(typed);
        SourceId[] sites = [.. Sites.Where(site => only is null || site == only)];
        if (query.IsEmpty || sites.Length == 0)
        {
            return [];
        }

        IReadOnlyList<GameInfo>[] found = await Task.WhenAll(sites.Select(site => FindOnAsync(site, query, cancellationToken)));
        return [.. found.SelectMany(games => games).Where(game => !_leftOut(game.Game))];
    }

    private async Task<IReadOnlyList<GameInfo>> FindOnAsync(SourceId site, NameQuery query, CancellationToken cancellationToken)
    {
        try
        {
            return await _lookup.FindGamesAsync(site, query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            // The site can't search by what was typed yet, such as a word too short: typing more finds them.
            return [];
        }
        catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
        {
            LogCantFind(_logger, site.Value, exception.Message);
            return [];
        }
        catch (Exception exception)
        {
            LogFindFailed(_logger, exception, site.Value);
            return [];
        }
    }
}
