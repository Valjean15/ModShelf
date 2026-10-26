using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Search;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;
using ModShelf.Sources.Nexus.Search;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IModSearch"/> half: the <c>SearchMods</c> query against the v2 GraphQL API.
/// </summary>
public sealed partial class NexusSource : IModSearch
{
    private static readonly string SearchModsQuery = ReadQuery("SearchMods.graphql");

    /// <exception cref="ArgumentException">The game isn't Nexus Mods'.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused the search.</exception>
    public async Task<SearchPage> SearchAsync(GameRef game, SearchQuery query, CancellationToken cancellationToken)
    {
        string domain = NexusLink.ForGame(game);
        ArgumentNullException.ThrowIfNull(query);

        NexusModsPage found = await _api.QueryAsync<NexusModsPage>(SearchModsQuery, NexusSearchVariables.For(domain, query), cancellationToken)
            .ConfigureAwait(false);
        NexusModsPage.Result mods = found.Mods ?? throw new HttpRequestException("Nexus Mods answered the search without its results.");

        // Only the asked game's mods, in case a node says otherwise.
        return new SearchPage(
            (mods.Nodes ?? [])
                .Where(node => node.Game?.DomainName is null || string.Equals(node.Game.DomainName, domain, StringComparison.OrdinalIgnoreCase))
                .Select(node => ToModInfo(game, domain, node)),
            mods.TotalCount,
            query.Offset);
    }

    private static ModInfo ToModInfo(GameRef game, string domain, NexusModNode node)
    {
        string id = node.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new ModInfo(
            new ModRef(game, id),
            string.IsNullOrWhiteSpace(node.Name) ? $"Mod {id}" : node.Name,
            node.Summary,
            node.Author,
            node.Version,
            pageUrl: new Uri(Website, $"{domain}/mods/{id}"),
            pictureUrl: WebLink(node.PictureUrl),
            updatedAt: node.UpdatedAt,
            isAvailable: node.Status is null || node.Status == "published",
            hasAdultContent: node.AdultContent == true,
            downloads: Count(node.Downloads),
            endorsements: Count(node.Endorsements));
    }

    /// <summary>
    /// The query in the file of that name under <c>Search/</c>, which ships inside the assembly.
    /// </summary>
    private static string ReadQuery(string fileName)
    {
        using Stream stream = typeof(NexusSource).Assembly.GetManifestResourceStream($"ModShelf.Sources.Nexus.Search.{fileName}")
            ?? throw new InvalidOperationException($"The query {fileName} isn't embedded in the assembly.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
