using ModShelf.Application.Requirements;
using ModShelf.Cli.Commands;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// <c>download</c>'s <c>withRequirements</c>: what each link's mod needs, read here, and the mods the library lacks sent
/// to the queue as the links are, each by its newest main file's link (ADR 0102).
/// </summary>
internal sealed partial class DownloadTools
{
    private const string Download = "download";

    /// <summary>
    /// What one link's mod needs, with the mods to download sent to the queue, but those <paramref name="queued"/> for
    /// another link already, which it adds to.
    /// </summary>
    private async Task<LinkRequirements> RequirementsAsync(
        string link,
        LinkDownloadResult linked,
        bool deep,
        HashSet<ModRef> queued,
        CancellationToken cancellationToken)
    {
        if (linked.Problem is not null)
        {
            const string NotRead = "The link couldn't be downloaded, so what its mod needs wasn't read.";
            return new LinkRequirements(link, false, [], NotRead, NotRead);
        }

        RequirementsPlan plan;
        try
        {
            plan = await requirements.PlanAsync(McpProblems.Link(link), deep, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ProblemOf(exception) is { } problem)
        {
            return new LinkRequirements(link, false, [], problem, $"What its mod needs couldn't be read: {problem}");
        }

        string site = CliText.SiteName(sources, plan.Mod.Game.Source);
        if (!plan.Listed)
        {
            return new LinkRequirements(link, false, [], null, $"{site} doesn't list what mods need, so only the file downloads.");
        }

        Dictionary<ModRef, string> names = plan.Requirements.Where(requirement => requirement.Mod is not null)
            .ToDictionary(requirement => requirement.Mod!, requirement => requirement.Name);
        List<RequirementEntry> entries = [];
        foreach (PlannedRequirement requirement in plan.Requirements)
        {
            RequirementEntry entry = new(
                requirement.Name,
                requirement.PageUrl?.AbsoluteUri,
                requirement.Depth > 1 ? names.GetValueOrDefault(requirement.NeededBy) : null,
                requirement.Depth,
                string.Empty,
                requirement.Notes,
                null,
                null,
                string.Empty);
            entries.Add(requirement.State switch
            {
                RequirementState.ToDownload when !queued.Add(requirement.Mod!) => entry with
                {
                    State = "downloading",
                    Message = "Another link's mod needs it too, so it downloads once, for that link.",
                },
                RequirementState.ToDownload => entry with
                {
                    State = Download,
                    File = requirement.File!.FileName,
                    Download = await DownloadOneAsync(requirement.File.PageUrl!.AbsoluteUri, cancellationToken).ConfigureAwait(false),
                    Message = $"Its newest main file, {requirement.File.Name}, downloads into the library: download says how.",
                },
                RequirementState.InLibrary => entry with { State = "inLibrary", Message = "The library has it already, so it isn't downloaded." },
                RequirementState.Downloading => entry with { State = "downloading", Message = "A download of it is in the queue already." },
                RequirementState.Elsewhere => entry with
                {
                    State = "elsewhere",
                    Message = $"It's off {site}, so ModShelf doesn't download it: the user gets it from its page.",
                },
                _ => entry with { State = "cantDownload", Message = $"It can't be downloaded: {requirement.Why}" },
            });
        }

        return new LinkRequirements(link, true, entries, null, Summarize(plan, entries, site));
    }

    /// <summary>
    /// The results and the requirements' downloads as the queue has them now, as <see cref="AsTheyAreNowAsync(List{LinkDownloadResult}, CancellationToken)"/>
    /// says.
    /// </summary>
    private async Task<(List<LinkDownloadResult> Results, List<LinkRequirements>? Required)> AsTheyAreNowAsync(
        List<LinkDownloadResult> results,
        List<LinkRequirements>? required,
        CancellationToken cancellationToken)
    {
        List<LinkDownloadResult> requirementDownloads = [.. (required ?? []).SelectMany(each => each.Requirements)
            .Select(requirement => requirement.Download).OfType<LinkDownloadResult>()];
        List<LinkDownloadResult> now = await AsTheyAreNowAsync([.. results, .. requirementDownloads], cancellationToken).ConfigureAwait(false);
        if (required is null)
        {
            return (now, null);
        }

        int next = results.Count;
        List<LinkRequirements> requiredNow = [];
        foreach (LinkRequirements each in required)
        {
            List<RequirementEntry> entries = [];
            foreach (RequirementEntry requirement in each.Requirements)
            {
                entries.Add(requirement.Download is null ? requirement : requirement with { Download = now[next++] });
            }

            requiredNow.Add(each with { Requirements = entries });
        }

        return (now[..results.Count], requiredNow);
    }

    /// <summary>
    /// What a link's mod needs, counted by what became of it.
    /// </summary>
    private static string Summarize(RequirementsPlan plan, List<RequirementEntry> entries, string site)
    {
        if (entries.Count == 0)
        {
            return $"{site} lists nothing mod {plan.Mod.Id} needs.";
        }

        int downloading = entries.Count(entry => entry is { State: Download, Download.Problem: null });
        int refused = entries.Count(entry => entry is { State: Download, Download.Problem: not null });
        int had = entries.Count(entry => entry.State is "inLibrary" or "downloading");
        int elsewhere = entries.Count(entry => entry.State == "elsewhere");
        int cant = entries.Count(entry => entry.State == "cantDownload");
        List<string> parts = [];
        if (downloading > 0)
        {
            parts.Add($"{downloading} downloading with it");
        }

        if (refused > 0)
        {
            parts.Add($"{refused} that couldn't be downloaded");
        }

        if (had > 0)
        {
            parts.Add($"{had} in the library or the queue already");
        }

        if (elsewhere > 0)
        {
            parts.Add($"{elsewhere} off the site, for the user to get from its page");
        }

        if (cant > 0)
        {
            parts.Add($"{cant} with no file to download");
        }

        string said = parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])}, and {parts[^1]}";
        return $"{site} lists {entries.Count} {(entries.Count == 1 ? "thing" : "things")} mod {plan.Mod.Id} needs: {said}. "
            + "The site's list can be wrong or out of date, so the mod's description is worth a look too. Nothing is installed.";
    }

    /// <summary>
    /// How many of what the mods need download too, for the call's message.
    /// </summary>
    private static string SummarizeRequirements(List<LinkRequirements> required)
    {
        int downloading = required.SelectMany(each => each.Requirements).Count(entry => entry is { State: Download, Download.Problem: null });
        string downloads = downloading switch
        {
            0 => "None of the mods they need downloads",
            1 => "1 mod they need downloads too",
            _ => $"{downloading} mods they need download too",
        };
        return $"{downloads}; requirements says what each link's mod needs, and what's off the site for the user to get.";
    }
}
