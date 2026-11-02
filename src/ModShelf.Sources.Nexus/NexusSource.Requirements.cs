using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Catalog;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IModRequirements"/> half: the <c>ModRequirements</c> query against the v2 GraphQL API. The mods a mod
/// needs from Nexus Mods are named by their game's number, which the source matches to its domain name from the games it
/// has read: the mod's own game, read with its page, or the list of games, read once for another game.
/// </summary>
public sealed partial class NexusSource : IModRequirements
{
    private static readonly string ModRequirementsQuery = ReadQuery("Catalog", "ModRequirements.graphql");

    /// <returns>
    /// What the mod needs, in the order Nexus Mods lists it: other mods, with their game and ID when their game can be
    /// named, and things elsewhere, with their page when it's a web link. Empty when Nexus Mods doesn't list the mod.
    /// </returns>
    /// <exception cref="ArgumentException">The mod isn't Nexus Mods'.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused the query.</exception>
    public async Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        NexusLink mod = NexusLink.ForMod(modRef);
        long id = long.Parse(mod.ModId, NumberStyles.None, CultureInfo.InvariantCulture);
        NexusRequirementsByDomain found = await _api
            .QueryAsync<NexusRequirementsByDomain>(ModRequirementsQuery, NexusModIds.For(mod.Game, id), cancellationToken)
            .ConfigureAwait(false);
        NexusRequirementsByDomain.ModNode? node = found.LegacyModsByDomain?.Nodes?.FirstOrDefault(listed => listed.ModId == id && IsOfGame(listed.Game, mod.Game));

        List<ModRequirement> needs = [];
        foreach (NexusRequirement? requirement in node?.ModRequirements?.NexusRequirements?.Nodes ?? [])
        {
            if (requirement is not null && await ToRequirementAsync(requirement, mod.Game, cancellationToken).ConfigureAwait(false) is { } need)
            {
                needs.Add(need);
            }
        }

        return needs;
    }

    /// <summary>
    /// The text, or null when it's blank, as the API gives an empty string for nothing.
    /// </summary>
    private static string? Named(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <returns>The requirement; null for one with nothing to call it by.</returns>
    private async Task<ModRequirement?> ToRequirementAsync(NexusRequirement requirement, string game, CancellationToken cancellationToken)
    {
        if (requirement.ExternalRequirement == true)
        {
            Uri? page = WebLink(requirement.Url);
            string? name = Named(requirement.ModName) ?? page?.Host;
            return name is null ? null : new ModRequirement(name, pageUrl: page, notes: requirement.Notes);
        }

        string? modId = long.TryParse(requirement.ModId, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0
            ? number.ToString(CultureInfo.InvariantCulture)
            : null;
        string? needed = modId is null ? null : await GameDomainAsync(requirement.GameId, game, cancellationToken).ConfigureAwait(false);
        string? called = Named(requirement.ModName) ?? (modId is null ? null : $"Mod {modId}");
        if (called is null)
        {
            return null;
        }

        ModRef? mod = needed is null ? null : new ModRef(new GameRef(NexusId, needed), modId!);
        return new ModRequirement(called, mod, notes: requirement.Notes);
    }

    /// <summary>
    /// The domain name of the game with the number: known from a game read before, or from the mod's own game, read now,
    /// or from the list of games, read now if it hasn't been. Null when none says, or they can't be read: the requirement
    /// is still listed, without a way to open it.
    /// </summary>
    private async Task<string?> GameDomainAsync(string? gameId, string modsGame, CancellationToken cancellationToken)
    {
        if (!long.TryParse(gameId, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
        {
            return null;
        }

        try
        {
            if (!_gameDomains.ContainsKey(number) && !_categories.ContainsKey(modsGame))
            {
                NexusGame found = await _api.GetAsync<NexusGame>($"{NexusLink.GameApiPath(modsGame)}.json", cancellationToken).ConfigureAwait(false);
                Remember(modsGame, found);
            }

            if (!_gameDomains.ContainsKey(number) && !_gamesListed)
            {
                _ = await GetGamesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException)
        {
            // Read again with the next mod's requirements.
        }

        return _gameDomains.GetValueOrDefault(number);
    }
}
