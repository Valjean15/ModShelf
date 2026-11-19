using Microsoft.Extensions.Logging;

using ModShelf.Application.Downloads;
using ModShelf.Application.Library;
using ModShelf.Application.Library.Ports;
using ModShelf.Application.Sources;
using ModShelf.Domain.Downloads;
using ModShelf.Domain.Library;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Requirements;

/// <summary>
/// Plans a mod's requirements from its site's <see cref="IModRequirements"/>, the library and the queue, and queues the
/// chosen ones through <see cref="ILibraryDownloads"/>, as Download does (ADR 0102).
/// </summary>
internal sealed partial class SiteRequirements(
    ISourceRegistry sources,
    ILibraryRepository library,
    IDownloadService downloads,
    ILibraryDownloads libraryDownloads,
    ILogger<SiteRequirements> logger) : IRequirementDownloads
{
    /// <summary>
    /// How deep "their requirements too" goes: what the mod needs, what those need, and what those need.
    /// </summary>
    public const int DeepestLevel = 3;

    public async Task<RequirementsPlan> PlanAsync(ModRef modRef, bool deeper, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        ModRef mod = modRef;
        if (sources.Find<IModRequirements>(mod.Game.Source) is not { } requirements)
        {
            return new RequirementsPlan(mod, [], Listed: false, deeper);
        }

        IModCatalog? catalog = sources.Find<IModCatalog>(mod.Game.Source);
        string site = requirements.DisplayName;
        HashSet<ModRef> inLibrary = [.. (await library.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(stored => stored.Files.Count > 0)
            .Select(stored => stored.Mod)];
        HashSet<ModRef> downloading = [.. (await downloads.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Status is not (DownloadJobStatus.Completed or DownloadJobStatus.Cancelled or DownloadJobStatus.Failed))
            .Select(job => job.File.Mod)];

        List<PlannedRequirement> planned = [];
        HashSet<ModRef> listed = [mod];
        HashSet<(string Name, Uri? Page)> elsewhere = [];
        Queue<(ModRef Mod, int Depth)> toRead = new([(mod, 1)]);
        while (toRead.TryDequeue(out (ModRef Mod, int Depth) reading))
        {
            IReadOnlyList<ModRequirement> needs;
            try
            {
                needs = await requirements.GetRequirementsAsync(reading.Mod, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (reading.Depth > 1 && exception is not OperationCanceledException)
            {
                // A deeper level that can't be read leaves out only what it would add; the mod itself is listed already.
                LogCantReadDeeper(logger, exception, reading.Mod.Id, reading.Mod.Game.Key);
                continue;
            }

            foreach (ModRequirement need in needs)
            {
                if (need.Mod is not { } needed)
                {
                    if (elsewhere.Add((need.Name, need.PageUrl)))
                    {
                        planned.Add(new PlannedRequirement(need.Name, null, need.PageUrl, need.Notes, reading.Depth, reading.Mod, RequirementState.Elsewhere));
                    }

                    continue;
                }

                if (!listed.Add(needed))
                {
                    continue;
                }

                PlannedRequirement requirement = new(need.Name, needed, need.PageUrl, need.Notes, reading.Depth, reading.Mod, RequirementState.ToDownload);
                if (inLibrary.Contains(needed))
                {
                    planned.Add(requirement with { State = RequirementState.InLibrary });
                    continue;
                }

                if (downloading.Contains(needed))
                {
                    planned.Add(requirement with { State = RequirementState.Downloading });
                    continue;
                }

                PlannedRequirement withFile = await WithFileAsync(requirement, catalog, site, cancellationToken).ConfigureAwait(false);
                planned.Add(withFile);
                if (deeper && reading.Depth < DeepestLevel && withFile.State == RequirementState.ToDownload)
                {
                    toRead.Enqueue((needed, reading.Depth + 1));
                }
            }
        }

        return new RequirementsPlan(mod, InTreeOrder(mod, planned), Listed: true, deeper);
    }

    public async Task<RequirementsPlan> PlanAsync(Uri link, bool deeper, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        RecognizedLink recognized = await sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false);
        return await PlanAsync(recognized.File.Mod, deeper, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RequirementDownloads> QueueAsync(IReadOnlyList<PlannedRequirement> chosen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        List<QueuedRequirement> queued = [];
        List<RequirementProblem> problems = [];
        foreach (PlannedRequirement requirement in chosen.Where(requirement => requirement is { State: RequirementState.ToDownload, File.PageUrl: not null }))
        {
            try
            {
                queued.Add(new QueuedRequirement(requirement, await libraryDownloads.AddAsync(requirement.File!.PageUrl!, cancellationToken).ConfigureAwait(false)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogCantQueue(logger, exception, requirement.Mod?.Id ?? requirement.Name, requirement.NeededBy.Game.Key);
                problems.Add(new RequirementProblem(requirement, exception));
            }
        }

        return new RequirementDownloads(queued, problems);
    }

    /// <summary>
    /// The requirements read level by level, so a mod needed twice is listed where it's needed first, put in the order of
    /// a tree: each mod followed by what it needs, the site's order kept within each.
    /// </summary>
    private static List<PlannedRequirement> InTreeOrder(ModRef root, List<PlannedRequirement> planned)
    {
        ILookup<ModRef, PlannedRequirement> needs = planned.ToLookup(requirement => requirement.NeededBy);
        List<PlannedRequirement> ordered = new(planned.Count);
        Stack<PlannedRequirement> toWalk = new(needs[root].Reverse());
        while (toWalk.TryPop(out PlannedRequirement? requirement))
        {
            ordered.Add(requirement);
            if (requirement.Mod is { } needed)
            {
                foreach (PlannedRequirement deeper in needs[needed].Reverse())
                {
                    toWalk.Push(deeper);
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// The mod's newest main file, as Browse's Download selected takes; or why there's none to download.
    /// </summary>
    private async Task<PlannedRequirement> WithFileAsync(PlannedRequirement requirement, IModCatalog? catalog, string site, CancellationToken cancellationToken)
    {
        if (catalog is null)
        {
            return requirement with { State = RequirementState.CantDownload, Why = $"{site} can't list its mods' files." };
        }

        IReadOnlyList<ModFile> files;
        try
        {
            files = await catalog.GetFilesAsync(requirement.Mod!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCantReadFiles(logger, exception, requirement.Mod!.Id, requirement.Mod.Game.Key);
            return requirement with { State = RequirementState.CantDownload, Why = $"{site} couldn't list its files: {exception.Message}" };
        }

        return files.Where(file => file.Category == ModFileCategory.Main && file.PageUrl is not null).MaxBy(file => file.UploadedAt) is { } newest
            ? requirement with { File = newest }
            : requirement with { State = RequirementState.CantDownload, Why = $"{site} lists no main file of it to download." };
    }
}
