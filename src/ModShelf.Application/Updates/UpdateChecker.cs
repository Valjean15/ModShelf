using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Application.Updates;

/// <summary>
/// Checks the watched mods game by game, through each source's update feed where it can, and keeps what it finds on
/// each mod, in a change of its own.
/// </summary>
internal sealed partial class UpdateChecker : IUpdateChecker, IDisposable
{
    /// <summary>
    /// How much earlier than a mod's last check a change still counts, for clocks that differ from the site's.
    /// </summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How much later than a feed's reach the oldest question goes, so the source's clock never finds it too early.
    /// </summary>
    private static readonly TimeSpan ReachSlack = TimeSpan.FromMinutes(1);

    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _library;
    private readonly LibraryChanges _changes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateChecker> _logger;
    private readonly SemaphoreSlim _checking = new(1, 1);

    public UpdateChecker(
        ISourceRegistry sources,
        ILibraryRepository library,
        LibraryChanges changes,
        TimeProvider timeProvider,
        ILogger<UpdateChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _library = library;
        _changes = changes;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        await _checking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Check check = new(_timeProvider.GetUtcNow());
            IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (IGrouping<GameRef, LibraryMod> game in library.Where(mod => mod.IsWatched).GroupBy(mod => mod.Mod.Game))
            {
                if (check.Stopped.TryGetValue(game.Key.Source, out string? stopped))
                {
                    check.Fail(game.Key, null, stopped);
                    continue;
                }

                await CheckGameAsync(check, game.Key, [.. game], cancellationToken).ConfigureAwait(false);
            }

            UpdateCheck result = new(check.Checked, check.Requests, check.Found, check.Failures);
            LogChecked(_logger, result.Checked, result.Requests, result.Found.Count, result.Failures.Count);
            return result;
        }
        finally
        {
            _checking.Release();
        }
    }

    public void Dispose() => _checking.Dispose();

    /// <summary>
    /// Whether a failure means the site wants nothing more for now: it refused the account, or asked to slow down; or it
    /// can't be reached at all, as when offline, where each question would wait out its retries and timeouts in turn.
    /// </summary>
    private static bool StopsTheSource(Exception exception)
        => exception is AuthenticationFailedException
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: null };

    /// <summary>
    /// When <paramref name="mod"/> was last checked, as far as can be told: a check later than <paramref name="now"/>,
    /// recorded before the clock moved back, is taken as none, and a mod never checked was last checked when it was added.
    /// </summary>
    private static DateTimeOffset LastChecked(LibraryMod mod, DateTimeOffset now)
        => mod.CheckedAt is { } checkedAt && checkedAt <= now ? checkedAt
            : mod.AddedAt <= now ? mod.AddedAt
            : now;

    private async Task CheckGameAsync(Check check, GameRef game, IReadOnlyList<LibraryMod> mods, CancellationToken cancellationToken)
    {
        if (_sources.Find<IModCatalog>(game.Source) is not { } catalog)
        {
            check.Fail(game, null, $"{game.Source} can't describe its mods, so they can't be checked for updates.");
            return;
        }

        // Mods checked within the feed's reach, less the margin, are asked about all at once. One checked later than now
        // is asked about on its own, since when it was checked isn't known.
        IUpdateFeed? feed = _sources.Find<IUpdateFeed>(game.Source);
        DateTimeOffset reach = feed is null ? DateTimeOffset.MaxValue : check.StartedAt - feed.Reach + ReachSlack;
        LibraryMod[] byFeed = [.. mods.Where(mod => mod.CheckedAt is { } checkedAt && checkedAt <= check.StartedAt && checkedAt - Margin >= reach)];
        List<LibraryMod> toAsk = [.. mods.Except(byFeed)];
        if (byFeed.Length > 0)
        {
            DateTimeOffset since = byFeed.Min(mod => mod.CheckedAt!.Value) - Margin;
            IReadOnlyList<ModUpdate> changed;
            try
            {
                check.Requests++;
                changed = await feed!.GetUpdatesAsync(game, since, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCantCheckGame(_logger, exception, game.Key, game.Source.Value);
                check.Fail(game, null, exception.Message);
                if (StopsTheSource(exception))
                {
                    check.Stop(game.Source, exception);
                }

                return;
            }

            Dictionary<ModRef, ModUpdate> byMod = [];
            foreach (ModUpdate update in changed)
            {
                byMod[update.Mod] = update;
            }

            foreach (LibraryMod mod in byFeed)
            {
                // A feed that doesn't say when the files changed is taken to mean they might have.
                if (byMod.TryGetValue(mod.Mod, out ModUpdate? update) && !(update.FilesUpdatedAt < mod.CheckedAt!.Value - Margin))
                {
                    toAsk.Add(mod);
                }
                else
                {
                    _changes.Publish(await _library.UpdateAsync(
                            mod.Mod,
                            stored => stored is { IsWatched: true } ? stored.Checked(check.StartedAt) : stored,
                            cancellationToken)
                        .ConfigureAwait(false));
                    check.Checked++;
                }
            }
        }

        foreach (LibraryMod mod in toAsk)
        {
            IReadOnlyList<ModFile> files;
            try
            {
                check.Requests++;
                files = await catalog.GetFilesAsync(mod.Mod, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogCantCheckMod(_logger, exception, mod.Mod.Id, game.Key, game.Source.Value);
                check.Fail(game, mod.Mod, exception.Message);
                if (StopsTheSource(exception))
                {
                    check.Stop(game.Source, exception);
                    return;
                }

                continue;
            }

            LibraryChange change = await _library.UpdateAsync(
                    mod.Mod,
                    stored => stored is { IsWatched: true }
                        ? stored.Checked(check.StartedAt).WithUpdates(
                            UpdateFinder.Find(stored, files, LastChecked(stored, check.StartedAt) - Margin, check.StartedAt))
                        : stored,
                    cancellationToken)
                .ConfigureAwait(false);
            _changes.Publish(change);
            if (change.After is { IsWatched: true } after)
            {
                check.Checked++;
                check.Found.AddRange(after.Updates.Where(update => change.Before?.Updates.All(earlier => earlier.File != update.File) ?? true));
            }
        }
    }

    /// <summary>
    /// What a check has done so far.
    /// </summary>
    private sealed class Check(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        public int Checked { get; set; }

        public int Requests { get; set; }

        public List<LibraryUpdate> Found { get; } = [];

        public List<UpdateCheckFailure> Failures { get; } = [];

        /// <summary>
        /// The sources not to ask again, and why.
        /// </summary>
        public Dictionary<SourceId, string> Stopped { get; } = [];

        public void Fail(GameRef game, ModRef? mod, string reason) => Failures.Add(new UpdateCheckFailure(game, mod, reason));

        public void Stop(SourceId source, Exception exception)
            => Stopped[source] = exception is HttpRequestException { StatusCode: null }
                ? $"Not checked: {source} couldn't be reached earlier in this check. {exception.Message}"
                : $"Not checked: {source} refused an earlier question in this check. {exception.Message}";
    }
}
