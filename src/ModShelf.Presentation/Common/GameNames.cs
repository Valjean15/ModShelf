using Microsoft.Extensions.Logging;

using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Presentation.Common;

/// <summary>
/// Games' names and pictures from their sites, for the screens that list mods by game. Until a site names a game, or when
/// it can't, the game goes by its key, such as skyrimspecialedition, with its initial for a cover.
/// </summary>
internal static partial class GameNames
{
    /// <summary>
    /// Asks the game's site for its name and where its pictures are. Null when its source doesn't name games, when the
    /// site can't say, which is logged as the caller's, or when <paramref name="cancellationToken"/> stops it.
    /// </summary>
    public static async Task<GameInfo?> ReadAsync(ISourceRegistry sources, GameRef game, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(logger);

        if (sources.Find<IModCatalog>(game.Source) is not { } catalog)
        {
            return null;
        }

        try
        {
            return await catalog.GetGameAsync(game, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is closing.
            return null;
        }
        catch (Exception exception) when (SiteProblems.IsSiteProblem(exception))
        {
            // Expected without a linked account, or offline: no stack trace for it.
            LogGameNameUnknown(logger, game.Key, game.Source.Value, exception.Message);
            return null;
        }
        catch (Exception exception)
        {
            LogGameNameFailed(logger, exception, game.Key, game.Source.Value);
            return null;
        }
    }
}
