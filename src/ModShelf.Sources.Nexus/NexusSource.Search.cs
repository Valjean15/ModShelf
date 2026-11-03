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
    private static readonly string SearchModsQuery = ReadQuery("Search", "SearchMods.graphql");

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
            (mods.Nodes ?? []).Where(node => IsOfGame(node.Game, domain)).Select(node => ToModInfo(new ModRef(game, ModIdOf(node)), domain, node)),
            mods.TotalCount,
            query.Offset);
    }

    /// <summary>
    /// Whether a node is of the game, or doesn't say.
    /// </summary>
    private static bool IsOfGame(NexusModNode.GameNode? node, string domain)
        => node?.DomainName is null || string.Equals(node.DomainName, domain, StringComparison.OrdinalIgnoreCase);

    private static string ModIdOf(NexusModNode node) => node.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A mod as the v2 API describes it, in a search or by its ID: with its category when it was asked for.
    /// </summary>
    private static ModInfo ToModInfo(ModRef mod, string domain, NexusModNode node)
    {
        string id = ModIdOf(node);
        return new ModInfo(
            mod,
            string.IsNullOrWhiteSpace(node.Name) ? $"Mod {id}" : node.Name,
            node.Summary,
            node.Author,
            node.Version,
            pageUrl: new NexusLink(domain, id, FileId: null, IsHandoff: false).PageUrl,
            pictureUrl: WebLink(node.PictureUrl),
            updatedAt: node.UpdatedAt,
            isAvailable: IsAvailable(available: null, node.Status),
            hasAdultContent: node.AdultContent == true,
            downloads: Count(node.Downloads),
            endorsements: Count(node.Endorsements),
            thumbnailUrl: WebLink(node.ThumbnailUrl),
            thumbnailBlurredUrl: WebLink(node.ThumbnailBlurredUrl),
            category: node.ModCategory?.Name ?? node.Category,
            size: node.FileSize is >= 0 and <= long.MaxValue / 1024 ? node.FileSize * 1024 : null,
            publishedAt: node.CreatedAt);
    }

    /// <summary>
    /// The query in the file of that name in the folder, such as <c>Search/</c>, which ships inside the assembly.
    /// </summary>
    private static string ReadQuery(string folder, string fileName)
    {
        using Stream stream = typeof(NexusSource).Assembly.GetManifestResourceStream($"ModShelf.Sources.Nexus.{folder}.{fileName}")
            ?? throw new InvalidOperationException($"The query {folder}/{fileName} isn't embedded in the assembly.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
