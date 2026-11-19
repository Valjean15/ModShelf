using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Thunderstore.Api;
using ModShelf.Sources.Thunderstore.Catalog;
using ModShelf.Sources.Thunderstore.Links;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// The <see cref="IModCatalog"/> half: the communities as games, and each package, its versions and its changelog.
/// </summary>
public sealed partial class ThunderstoreSource : IModCatalog
{
    // The communities have come in one page so far; a list that never ends stops here.
    private const int MostCommunityPages = 20;

    /// <summary>
    /// Never: a community's identifier, such as <c>lethal-company</c> or <c>repo</c>, looks like a Nexus Mods game's key,
    /// which a key typed alone has always meant, so Thunderstore's games are named with their site, as
    /// <c>thunderstore/repo</c> (ADR 0103).
    /// </summary>
    public bool CouldBeGameKey(string key) => false;

    /// <returns>Every community Thunderstore lists, from one request, read once a run.</returns>
    /// <exception cref="HttpRequestException">Thunderstore couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
        => [.. (await CommunitiesAsync(cancellationToken).ConfigureAwait(false)).Select(community => Describe(community, categories: null))];

    /// <returns>The community, with its categories.</returns>
    /// <exception cref="ArgumentException">The game isn't Thunderstore's.</exception>
    /// <exception cref="HttpRequestException">Thunderstore has no such community (404), couldn't be reached, or refused.</exception>
    public async Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        string key = Community(game);
        ThunderstoreCommunity community = (await CommunitiesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(listed => string.Equals(listed.Identifier, key, StringComparison.Ordinal))
            ?? throw new HttpRequestException($"Thunderstore has no community '{key}'.", null, HttpStatusCode.NotFound);
        return Describe(community, await CategoriesAsync(key, cancellationToken).ConfigureAwait(false));
    }

    /// <returns>The package as its community lists it.</returns>
    /// <exception cref="ArgumentException">The mod isn't Thunderstore's.</exception>
    /// <exception cref="HttpRequestException">The community doesn't list the package (404), or its listing couldn't be read.</exception>
    public async Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ListedPackage package = await ListedAsync(modRef, cancellationToken).ConfigureAwait(false);
        return Describe(modRef.Game.Key, package);
    }

    /// <returns>Each listed version as a file: the newest the main one, each older one an old version.</returns>
    /// <inheritdoc cref="GetModAsync" path="/exception"/>
    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ListedPackage package = await ListedAsync(modRef, cancellationToken).ConfigureAwait(false);
        return ThunderstoreFiles.Of(ModOf(modRef.Game.Key, package.Name), package);
    }

    /// <returns>
    /// The changes the newest version's <c>CHANGELOG.md</c> lists under each version's heading; empty when it has none, or
    /// names no version.
    /// </returns>
    /// <inheritdoc cref="GetModAsync" path="/exception"/>
    public async Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ListedPackage package = await ListedAsync(modRef, cancellationToken).ConfigureAwait(false);
        string path = $"api/experimental/package/{Uri.EscapeDataString(package.Name.Team)}/{Uri.EscapeDataString(package.Name.Name)}"
            + $"/{Uri.EscapeDataString(package.Newest.Number)}/changelog/";
        ThunderstoreMarkdown changelog;
        try
        {
            changelog = await _api.GetAsync<ThunderstoreMarkdown>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Listed a moment ago, and gone since; or the version's changelog alone is missing.
            return [];
        }

        return ThunderstoreChangelogs.Parse(changelog.Markdown);
    }

    private static GameInfo Describe(ThunderstoreCommunity community, IReadOnlyList<string>? categories)
        => new(
            GameOf(community.Identifier),
            community.Name,
            ThunderstoreLink.CommunityPage(community.Identifier),
            categories,
            coverUrl: WebLink(community.HeroImageUrl) ?? WebLink(community.BackgroundImageUrl),
            iconUrl: WebLink(community.CommunityIconUrl));

    /// <summary>
    /// A picture's link, when it's an HTTPS one.
    /// </summary>
    private static Uri? WebLink(string? link)
        => Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    /// <summary>
    /// Every listed community, read once a run, following the list's pages, of which there's been one so far.
    /// </summary>
    private async Task<IReadOnlyList<ThunderstoreCommunity>> CommunitiesAsync(CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<ThunderstoreCommunity>> reading;
        lock (_communitiesGate)
        {
            reading = _communities ??= ReadCommunitiesAsync();
        }

        try
        {
            return await reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_communitiesGate)
            {
                if (_communities == reading)
                {
                    _communities = null;
                }
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<ThunderstoreCommunity>> ReadCommunitiesAsync()
    {
        // Not the first caller's token: the read is everyone's, and its requests have their own time limits.
        List<ThunderstoreCommunity> communities = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        ThunderstoreCommunities page = await _api.GetAsync<ThunderstoreCommunities>("api/cyberstorm/community/", CancellationToken.None).ConfigureAwait(false);
        for (int pages = 1; ; pages++)
        {
            communities.AddRange(page.Results.Where(community => IsCommunity(community.Identifier)
                && !string.IsNullOrWhiteSpace(community.Name)
                && seen.Add(community.Identifier)));
            if (page.Next is not { } next || !Uri.TryCreate(next, UriKind.Absolute, out Uri? link) || pages >= MostCommunityPages)
            {
                break;
            }

            page = await _api.GetAsync<ThunderstoreCommunities>(link, CancellationToken.None).ConfigureAwait(false);
        }

        LogCommunitiesRead(_logger, communities.Count);
        return communities;
    }

    /// <summary>
    /// The community's categories, by name, read once a run.
    /// </summary>
    private async Task<IReadOnlyList<string>> CategoriesAsync(string community, CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<string>> reading = _categories.GetOrAdd(community, ReadCategoriesAsync);
        try
        {
            return await reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _categories.TryRemove(new KeyValuePair<string, Task<IReadOnlyList<string>>>(community, reading));
            throw;
        }
    }

    private async Task<IReadOnlyList<string>> ReadCategoriesAsync(string community)
    {
        ThunderstoreFilters filters = await _api.GetAsync<ThunderstoreFilters>(
                $"api/cyberstorm/community/{Uri.EscapeDataString(community)}/filters/",
                CancellationToken.None)
            .ConfigureAwait(false);
        return [.. filters.PackageCategories.Select(category => category.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
