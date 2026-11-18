using Microsoft.Extensions.Logging;

using ModShelf.Application.Library;
using ModShelf.Application.Sources;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Catalog;

namespace ModShelf.Application.Lists;

/// <summary>
/// Exports the library as a list and imports lists into it (ADR 0098), through the library and its downloads, as the
/// screens and commands that watch mods and queue files one at a time do.
/// </summary>
internal sealed partial class ModLists(
    IModLibrary library,
    ILibraryDownloads downloads,
    ISourceRegistry sources,
    TimeProvider timeProvider,
    ILogger<ModLists> logger) : IModLists
{
    public async Task<ModList> ExportAsync(ListScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        IReadOnlyList<LibraryMod> mods = await library.ListAsync(cancellationToken).ConfigureAwait(false);
        return new ModList(
            null,
            timeProvider.GetUtcNow(),
            [
                .. mods.Where(mod => scope.Takes(mod.Mod))
                    .OrderBy(mod => mod.Mod.Game.Source.Value, StringComparer.Ordinal)
                    .ThenBy(mod => mod.Mod.Game.Key, StringComparer.Ordinal)
                    .ThenBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(mod => mod.Mod.Id, StringComparer.Ordinal)
                    .Select(Listed),
            ]);
    }

    public string Write(ModList list) => ModListFormat.Write(list);

    public ModList Read(string text) => ModListFormat.Read(text, timeProvider.GetUtcNow());

    public async Task<ListImportPlan> PlanAsync(ModList list, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);

        Dictionary<ModRef, LibraryMod> stored = (await library.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(mod => mod.Mod);
        return new ListImportPlan(list, [.. list.Mods.Select(listed => Planned(listed, stored.GetValueOrDefault(listed.Mod)))]);
    }

    public async Task<ListImportResult> ImportAsync(
        ListImportPlan plan,
        ListImportOptions options,
        IProgress<int>? progress,
        Func<Uri, CancellationToken, Task<bool>>? queue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        Tally tally = new(queue ?? QueueHereAsync);
        for (int index = 0; index < plan.Mods.Count; index++)
        {
            await ImportAsync(plan.Mods[index], options, tally, cancellationToken).ConfigureAwait(false);
            progress?.Report(index + 1);
        }

        LogImported(logger, plan.Mods.Count, tally.Added, tally.Queued.Count, tally.Problems.Count);
        return new ListImportResult(tally.Added, tally.Queued, tally.InLibrary, tally.Problems);
    }

    private static ListedMod Listed(LibraryMod mod)
        => new(
            mod.Mod,
            mod.Name,
            mod.Author,
            mod.PageUrl,
            mod.IsWatched,
            mod.IsPinned,
            mod.Note,
            [
                .. mod.Files
                    .Where(file => !OldVersions.IsArchived(file.RelativePath))
                    .Select(file => new ListedFile(file.File, file.Name, file.VersionLabel, file.Category, Optional: false)),
            ]);

    private static ModFile? NewestMainFile(IReadOnlyList<ModFile> files)
        => files.Where(file => file.Category == ModFileCategory.Main && file.PageUrl is not null).MaxBy(file => file.UploadedAt);

    private async Task<bool> QueueHereAsync(Uri link, CancellationToken cancellationToken)
        => (await downloads.AddAsync(link, cancellationToken).ConfigureAwait(false)).Queued is not null;

    private PlannedMod Planned(ListedMod listed, LibraryMod? stored)
    {
        PlannedModState state = sources.Find<IModCatalog>(listed.Mod.Game.Source) is null
            ? PlannedModState.NoSite
            : stored is null ? PlannedModState.New : PlannedModState.InLibrary;
        return new PlannedMod(
            listed,
            state,
            [.. listed.Files.Select(file => new PlannedFile(file, stored?.Files.Any(kept => kept.File == file.File) ?? false))]);
    }

    private async Task ImportAsync(PlannedMod planned, ListImportOptions options, Tally tally, CancellationToken cancellationToken)
    {
        ListedMod listed = planned.Listed;
        if (planned.State == PlannedModState.NoSite || sources.Find<IModCatalog>(listed.Mod.Game.Source) is not { } catalog)
        {
            tally.Problem(listed, null, ListImportProblemKind.NoSite, null);
            return;
        }

        if (planned.State == PlannedModState.New && (listed.Watched || !options.Download))
        {
            switch (await WatchAsync(listed, tally, cancellationToken).ConfigureAwait(false))
            {
                case null:
                    tally.Added++;
                    break;

                // The site couldn't be asked about the mod: it's said once, not again for each file.
                case ListImportProblemKind.SiteFailed:
                    return;
            }
        }

        if (!options.Download)
        {
            return;
        }

        PlannedFile[] wanted = [.. planned.Files.Where(file => options.IncludeOptional || !file.Listed.Optional)];
        tally.InLibrary += wanted.Count(file => file.InLibrary);
        PlannedFile[] missing = [.. wanted.Where(file => !file.InLibrary)];
        if (missing.Length == 0)
        {
            return;
        }

        IReadOnlyList<ModFile> siteFiles;
        try
        {
            siteFiles = await catalog.GetFilesAsync(listed.Mod, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Unexpected(exception, listed.Mod);
            tally.Problem(listed, null, ListImportProblemKind.SiteFailed, exception.Message);
            return;
        }

        foreach (PlannedFile file in missing)
        {
            ModFile? site = siteFiles.FirstOrDefault(candidate => candidate.Reference == file.Listed.File)
                ?? (options.NewestForMissing ? NewestMainFile(siteFiles) : null);
            if (site?.PageUrl is not { } link)
            {
                tally.Problem(listed, file.Listed.File, site is null ? ListImportProblemKind.FileGone : ListImportProblemKind.NotDone, null);
                continue;
            }

            // The newest main file stands in for several files the site no longer has: it's queued once.
            if (!tally.Links.Add(link))
            {
                continue;
            }

            try
            {
                if (await tally.Queue(link, cancellationToken).ConfigureAwait(false))
                {
                    tally.Queued.Add(site.Reference);
                }
                else
                {
                    tally.InLibrary++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Unexpected(exception, listed.Mod);
                tally.Problem(listed, file.Listed.File, ListImportProblemKind.NotDone, exception.Message);
            }
        }
    }

    /// <returns>
    /// Null when the mod was watched, though a pin or note that couldn't be kept is said; otherwise why it wasn't, which
    /// is said too.
    /// </returns>
    private async Task<ListImportProblemKind?> WatchAsync(ListedMod listed, Tally tally, CancellationToken cancellationToken)
    {
        try
        {
            await library.WatchAsync(listed.Mod, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Unexpected(exception, listed.Mod);
            ListImportProblemKind kind = exception is HttpRequestException or AuthenticationFailedException
                ? ListImportProblemKind.SiteFailed
                : ListImportProblemKind.NotDone;
            tally.Problem(listed, null, kind, exception.Message);
            return kind;
        }

        try
        {
            if (listed.Pinned)
            {
                await library.SetPinnedAsync(listed.Mod, true, cancellationToken).ConfigureAwait(false);
            }

            if (listed.Note is { } note)
            {
                await library.SetNoteAsync(listed.Mod, note, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Unexpected(exception, listed.Mod);
            tally.Problem(listed, null, ListImportProblemKind.NotDone, exception.Message);
        }

        return null;
    }

    /// <summary>
    /// Logs a failure the sites and the library don't say they throw.
    /// </summary>
    private void Unexpected(Exception exception, ModRef mod)
    {
        if (exception is not (HttpRequestException or AuthenticationFailedException or ArgumentException or IOException
            or InvalidDataException or InvalidOperationException or KeyNotFoundException or Downloads.UnsupportedLinkException
            or OperationCanceledException))
        {
            LogImportFailed(logger, exception, mod.Id, mod.Game.ToString());
        }
    }

    /// <summary>
    /// What an import has done so far.
    /// </summary>
    private sealed class Tally(Func<Uri, CancellationToken, Task<bool>> queue)
    {
        public Func<Uri, CancellationToken, Task<bool>> Queue { get; } = queue;

        public int Added { get; set; }

        public List<ModFileRef> Queued { get; } = [];

        public int InLibrary { get; set; }

        public List<ListImportProblem> Problems { get; } = [];

        public HashSet<Uri> Links { get; } = [];

        public void Problem(ListedMod mod, ModFileRef? file, ListImportProblemKind kind, string? detail)
            => Problems.Add(new ListImportProblem(mod.Mod, mod.Name, file, kind, detail));
    }
}
