using Microsoft.Extensions.DependencyInjection;

using ModShelf.Application.Downloads;
using ModShelf.Application.Requirements;
using ModShelf.Application.Sources;
using ModShelf.Cli.Hosting;
using ModShelf.Domain.Mods;

namespace ModShelf.Cli.Commands;

/// <summary>
/// <c>--with-requirements</c>: what the file's mod needs, listed, and with <c>--yes</c>, the mods the library lacks
/// queued for it, each by its newest main file (ADR 0102).
/// </summary>
internal static partial class DownloadCommand
{
    /// <summary>
    /// Lists what the link's mod needs; with --yes, or when nothing's to download, queues the mods to download.
    /// </summary>
    /// <returns>
    /// An exit code to end with, such as after only listing them; or the requirements' downloads to follow, and whether any
    /// couldn't be queued.
    /// </returns>
    private static async Task<(int? ExitCode, IReadOnlyList<DownloadJobSnapshot> Jobs, bool Failed)> RequirementsAsync(
        CliEnvironment environment,
        IServiceProvider services,
        Uri link,
        Requiring requiring,
        CancellationToken cancellationToken)
    {
        ISourceRegistry sources = services.GetRequiredService<ISourceRegistry>();
        IRequirementDownloads requirements = services.GetRequiredService<IRequirementDownloads>();
        RequirementsPlan plan;
        try
        {
            plan = await requirements.PlanAsync(link, requiring.Deep, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && Problem(exception, library: true) is { } problem)
        {
            await environment.Error.WriteLineAsync(problem.Message).ConfigureAwait(false);
            return (problem.ExitCode, [], false);
        }

        string site = CliText.SiteName(sources, plan.Mod.Game.Source);
        WritePlan(environment.Output, plan, site);
        if (plan.ToDownload.Count == 0)
        {
            return (null, [], false);
        }

        if (!requiring.Yes)
        {
            environment.Output.WriteLine(
                "Run the command again with --yes to download it with them, or without --with-requirements to download it alone.");
            return (ExitCodes.Done, [], false);
        }

        RequirementDownloads queued = await requirements.QueueAsync(plan.ToDownload, cancellationToken).ConfigureAwait(false);
        foreach (RequirementProblem problem in queued.Problems)
        {
            string said = Problem(problem.Error, library: true)?.Message ?? problem.Error.Message;
            await environment.Error.WriteLineAsync($"{problem.Requirement.Name} couldn't be downloaded: {said}").ConfigureAwait(false);
        }

        return (null, [.. queued.Queued.Select(done => done.Download.Queued?.Job).OfType<DownloadJobSnapshot>()], queued.Problems.Count > 0);
    }

    private static void WritePlan(TextWriter output, RequirementsPlan plan, string site)
    {
        if (!plan.Listed)
        {
            output.WriteLine($"{site} doesn't list what mods need, so only the file downloads.");
            return;
        }

        if (plan.Requirements.Count == 0)
        {
            output.WriteLine($"{site} lists nothing mod {plan.Mod.Id} of {plan.Mod.Game.Key} needs.");
            return;
        }

        output.WriteLine($"What mod {plan.Mod.Id} of {plan.Mod.Game.Key} needs, as {site} lists it:");
        Dictionary<ModRef, string> names = plan.Requirements.Where(requirement => requirement.Mod is not null)
            .ToDictionary(requirement => requirement.Mod!, requirement => requirement.Name);
        foreach (PlannedRequirement requirement in plan.Requirements)
        {
            string indent = new(' ', 2 * requirement.Depth);
            string neededBy = requirement.Depth > 1 && names.TryGetValue(requirement.NeededBy, out string? by) ? $" (needed by {by})" : string.Empty;
            output.WriteLine($"{indent}{requirement.Name}{neededBy}: {Said(requirement)}");
            if (requirement.Notes is { } notes)
            {
                output.WriteLine($"{indent}  {notes}");
            }
        }

        output.WriteLine("The site's list can be wrong or out of date: check the mod's description too. Nothing is installed.");
    }

    private static string Said(PlannedRequirement requirement) => requirement.State switch
    {
        RequirementState.ToDownload => $"downloads its newest main file, {requirement.File!.Name} (file {requirement.File.Reference.Id} of mod {requirement.Mod!.Id})",
        RequirementState.InLibrary => "in the library already",
        RequirementState.Downloading => "downloading already",
        RequirementState.Elsewhere => requirement.PageUrl is { } page ? $"off the site, at {page.AbsoluteUri}" : "off the site",
        _ => $"can't be downloaded: {requirement.Why}",
    };

    /// <summary>
    /// What --with-requirements, --deep and --yes asked.
    /// </summary>
    private sealed record Requiring(bool With, bool Deep, bool Yes);
}
