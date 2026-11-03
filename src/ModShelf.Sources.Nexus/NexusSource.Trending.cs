using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="ITrendingMods"/> half: the game's trending mods from the v1 API, which the v2 API doesn't list.
/// </summary>
public sealed partial class NexusSource : ITrendingMods
{
    /// <summary>
    /// The game's trending mods from <c>v1/games/{game}/mods/trending.json</c>: as v1 describes a mod, without
    /// thumbnails, and with its category's name only when the game's categories were read already.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't Nexus Mods'.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached or refused.</exception>
    public async Task<IReadOnlyList<ModInfo>> GetTrendingAsync(GameRef game, CancellationToken cancellationToken)
    {
        string domain = NexusLink.ForGame(game);
        NexusMod?[] found = await _api.GetAsync<NexusMod?[]>($"{NexusLink.GameApiPath(domain)}/mods/trending.json", cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<long, string> categories = _categories.GetValueOrDefault(domain) ?? new Dictionary<long, string>();
        HashSet<long> listed = [];
        List<ModInfo> trending = [];
        foreach (NexusMod? mod in found)
        {
            // Only the asked game's mods, each once, in case the list says otherwise.
            if (mod is null
                || (mod.DomainName is { } other && !string.Equals(other, domain, StringComparison.OrdinalIgnoreCase))
                || NexusLink.FromApi(domain, mod.ModId) is not { } link
                || !listed.Add(mod.ModId))
            {
                continue;
            }

            string? category = mod.CategoryId is { } id ? categories.GetValueOrDefault(id) : null;
            trending.Add(ToModInfo(new ModRef(game, link.ModId), link, mod, category));
        }

        return trending;
    }
}
