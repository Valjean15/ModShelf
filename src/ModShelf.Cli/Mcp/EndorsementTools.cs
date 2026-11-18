using System.ComponentModel;

using ModelContextProtocol.Server;

using ModShelf.Application.Endorsements;
using ModShelf.Application.Sources;
using ModShelf.Cli.Commands;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// The MCP tool that endorses a mod with the linked account (ADR 0100), one mod at a time, by its link: an agent can't
/// endorse in bulk.
/// </summary>
[McpServerToolType]
internal sealed class EndorsementTools(IEndorsements endorsements, ISourceRegistry sources)
{
    [McpServerTool(Name = "mod_endorse", Title = "Endorse a mod", Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Endorses one mod on its site with the linked account, as the site's own button does, such as Endorse on Nexus Mods; with abstain, chooses not to endorse it, or takes the endorsement back. Takes one mod's link: endorse only the mods the user asks to. The site may refuse, such as for a mod the account didn't download, or its own; the error says why in its words.")]
    public Task<EndorseResult> EndorseAsync(
        [Description("A link to the mod or one of its files, such as its page on Nexus Mods.")] string link,
        [Description("Choose not to endorse the mod, or take the endorsement back, instead.")] bool abstain = false,
        CancellationToken cancellationToken = default)
        => McpProblems.GuardAsync(async () =>
        {
            Endorsement endorsement = abstain ? Endorsement.Abstained : Endorsement.Endorsed;
            ModRef mod = await endorsements.EndorseAsync(McpProblems.Link(link), endorsement, cancellationToken).ConfigureAwait(false);
            string site = CliText.SiteName(sources, mod.Game.Source);
            return new EndorseResult(
                mod.Game.Source.Value,
                mod.Game.Key,
                mod.Id,
                abstain ? "abstained" : "endorsed",
                abstain ? $"Abstained from mod {mod.Id} of {mod.Game.Key} on {site}." : $"Endorsed mod {mod.Id} of {mod.Game.Key} on {site}.");
        });
}
