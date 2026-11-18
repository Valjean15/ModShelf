using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Settings;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Updates;

namespace ModShelf.Application.Updates;

/// <summary>
/// Checks the watched mods game by game, through each source's update feed where it can, and keeps what it finds on
/// each mod, in a change of its own. A mod whose files its site won't list, and which the site then says is hidden or
/// removed, is marked gone from it rather than failed, and asked about again weekly, until it's back (ADR 0099).
/// </summary>
internal sealed partial class UpdateChecker : IUpdateChecker, IDisposable
{
    /// <summary>
    /// How much earlier than a mod's last check a change still counts, for clocks that differ from the site's.
    /// </summary>
    internal static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a mod gone from its site goes before its site is asked about it again.
    /// </summary>
    internal static readonly TimeSpan GoneRecheck = TimeSpan.FromDays(7);

    /// <summary>
    /// How much later than a feed's reach the oldest question goes, so the source's clock never finds it too early.
    /// </summary>
    private static readonly TimeSpan ReachSlack = TimeSpan.FromMinutes(1);

    private readonly ISourceRegistry _sources;
    private readonly ILibraryRepository _library;
    private readonly ISettingsService _settings;
    private readonly LibraryChanges _changes;
    private readonly LibraryHistory _history;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateChecker> _logger;
    private readonly SemaphoreSlim _checking = new(1, 1);

    public UpdateChecker(
        ISourceRegistry sources,
        ILibraryRepository library,
        ISettingsService settings,
        LibraryChanges changes,
        LibraryHistory history,
        TimeProvider timeProvider,
        ILogger<UpdateChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _library = library;
        _settings = settings;
        _changes = changes;
        _history = history;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        AppSettings? settings;
        try
        {
            settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            LogSettingsUnreadable(_logger, exception);
            settings = null;
        }

        return await CheckWhereAsync(game => settings?.ChecksUpdatesOf(game) ?? true, cancellationToken).ConfigureAwait(false);
    }

    public Task<UpdateCheck> CheckAsync(IReadOnlyCollection<GameRef> games, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(games);

        HashSet<GameRef> asked = [.. games];
        return CheckWhereAsync(asked.Contains, cancellationToken);
    }

    public void Dispose() => _checking.Dispose();

    /// <summary>
    /// Checks the watched mods of the games <paramref name="checks"/> says to.
    /// </summary>
    private async Task<UpdateCheck> CheckWhereAsync(Func<GameRef, bool> checks, CancellationToken cancellationToken)
    {
        await _checking.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Check check = new(_timeProvider.GetUtcNow());
            IReadOnlyList<LibraryMod> library = await _library.ListAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<LibraryMod> due = library.Where(mod => mod.IsWatched && checks(mod.Mod.Game) && IsDue(mod, check.StartedAt));
            foreach (IGrouping<GameRef, LibraryMod> game in due.GroupBy(mod => mod.Mod.Game))
            {
                if (check.Stopped.TryGetValue(game.Key.Source, out string? stopped))
                {
                    check.Fail(game.Key, null, stopped);
                    continue;
                }

                await CheckGameAsync(check, game.Key, [.. game], cancellationToken).ConfigureAwait(false);
            }

            UpdateCheck result = new(check.Checked, check.Requests, check.Found, check.Failures) { Gone = check.Gone, Back = check.Back };
            LogChecked(_logger, result.Checked, result.Requests, result.Found.Count, result.Failures.Count);
            return result;
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>
    /// Whether a failure means the site wants nothing more for now: it refused the account, or asked to slow down; or it
    /// can't be reached at all, as when offline, where each question would wait out its retries and timeouts in turn.
    /// </summary>
    private static bool StopsTheSource(Exception exception)
        => exception is AuthenticationFailedException
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: null };

    /// <summary>
    /// Whether a mod is checked now: any that's on its site; one gone from it only once a week has passed since its
    /// last check, since its site was asked about it then.
    /// </summary>
    private static bool IsDue(LibraryMod mod, DateTimeOffset now) => !mod.IsGone || now - LastChecked(mod, now) >= GoneRecheck;

    /// <summary>
    /// Whether a failure to list a mod's files might mean the site no longer has the mod, so it's worth asking.
    /// </summary>
    private static bool MightBeGone(Exception exception)
        => exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden };

    /// <summary>
    /// When <paramref name="mod"/> was last checked, as far as can be told: a check later than <paramref name="now"/>,
    /// recorded before the clock moved back, is taken as none, and a mod never checked was last checked when it was added.
    /// </summary>
    private static DateTimeOffset LastChecked(LibraryMod mod, DateTimeOffset now)
        => mod.CheckedAt is { } checkedAt && checkedAt <= now ? checkedAt
            : mod.AddedAt <= now ? mod.AddedAt
            : now;

    /// <summary>
    /// Whether the site says the mod is gone, asked once when it wouldn't list the mod's files: it describes the mod as
    /// hidden or removed, or doesn't know it at all. Anything else, such as a site that can't be reached, isn't taken
    /// to mean so, and the check fails as it would have.
    /// </summary>
    private static async Task<bool> IsGoneAsync(IModCatalog catalog, ModRef mod, Check check, CancellationToken cancellationToken)
    {
        try
        {
            check.Requests++;
            ModInfo info = await catalog.GetModAsync(mod, cancellationToken).ConfigureAwait(false);
            return !info.IsAvailable;
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or AuthenticationFailedException or ArgumentException)
        {
            return false;
        }
    }

    private async Task CheckGameAsync(Check check, GameRef game, IReadOnlyList<LibraryMod> mods, CancellationToken cancellationToken)
    {
        if (_sources.Find<IModCatalog>(game.Source) is not { } catalog)
        {
            check.Fail(game, null, $"{game.Source} can't describe its mods, so they can't be checked for updates.");
            return;
        }

        // Mods checked within the feed's reach, less the margin, are asked about all at once. One checked later than now
        // is asked about on its own, since when it was checked isn't known; so is one whose library side changed since
        // its check, since the feed only says what changed on the site.
        // The feed takes the question by the site's clock when it's asked, which may be minutes into the check.
        IUpdateFeed? feed = _sources.Find<IUpdateFeed>(game.Source);
        DateTimeOffset reach = feed is null ? DateTimeOffset.MaxValue : _timeProvider.GetUtcNow() - feed.Reach + ReachSlack;
        LibraryMod[] byFeed =
        [
            .. mods.Where(mod => !mod.NeedsFullCheck
                && !mod.IsGone
                && mod.CheckedAt is { } checkedAt
                && checkedAt <= check.StartedAt
                && checkedAt - Margin >= reach),
        ];
        List<LibraryMod> toAsk = [.. mods.Except(byFeed)];
        if (byFeed.Length > 0)
        {
            DateTimeOffset since = byFeed.Min(mod => mod.CheckedAt!.Value) - Margin;
            IReadOnlyList<ModUpdate>? changed;
            try
            {
                check.Requests++;
                changed = await feed!.GetUpdatesAsync(game, since, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                // The feed's clock found the question beyond its reach after all: its mods are asked about one by one.
                LogFeedOutOfReach(_logger, exception, game.Key, game.Source.Value);
                toAsk.AddRange(byFeed);
                changed = null;
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
            foreach (ModUpdate update in changed ?? [])
            {
                byMod[update.Mod] = update;
            }

            foreach (LibraryMod mod in changed is null ? [] : byFeed)
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
                if (MightBeGone(exception) && await IsGoneAsync(catalog, mod.Mod, check, cancellationToken).ConfigureAwait(false))
                {
                    await LeftSiteAsync(check, mod.Mod, cancellationToken).ConfigureAwait(false);
                    continue;
                }

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
                        ? stored.Checked(check.StartedAt).BackOnSite().WithUpdates(
                            UpdateFinder.Find(stored, files, LastChecked(stored, check.StartedAt) - Margin, check.StartedAt, catalog.FilesSideBySide))
                        : stored,
                    cancellationToken)
                .ConfigureAwait(false);
            _changes.Publish(change);
            if (change is { Before.IsGone: true, After: { IsGone: false } back })
            {
                LogBackOnSite(_logger, mod.Mod.Id, game.Key, game.Source.Value);
                check.Back.Add(back);
                await _history.RecordAsync([new LibraryEvent(check.StartedAt, LibraryEventKind.BackOnSite, back.Mod, back.Name)], CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (change.After is { IsWatched: true } after)
            {
                check.Checked++;
                // A pinned mod's are kept, but not offered.
                check.Found.AddRange(after.OfferedUpdates.Where(update => change.Before?.Updates.All(earlier => earlier.File != update.File) ?? true));
            }
        }
    }

    /// <summary>
    /// Marks the mod gone from its site, checked now; and, the first time, keeps that in its history and in the check.
    /// </summary>
    private async Task LeftSiteAsync(Check check, ModRef mod, CancellationToken cancellationToken)
    {
        LibraryChange change = await _library.UpdateAsync(
                mod,
                stored => stored is { IsWatched: true } ? stored.Checked(check.StartedAt).LeftSite(check.StartedAt) : stored,
                cancellationToken)
            .ConfigureAwait(false);
        _changes.Publish(change);
        if (change.After is not { IsWatched: true } after)
        {
            return;
        }

        check.Checked++;
        if (change.Before?.IsGone != true)
        {
            LogLeftSite(_logger, mod.Id, mod.Game.Key, mod.Game.Source.Value);
            check.Gone.Add(after);
            await _history.RecordAsync([new LibraryEvent(check.StartedAt, LibraryEventKind.LeftSite, after.Mod, after.Name)], CancellationToken.None)
                .ConfigureAwait(false);
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

        public List<LibraryMod> Gone { get; } = [];

        public List<LibraryMod> Back { get; } = [];

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
