using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Catalog;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// The <see cref="IModCatalog"/> half: CurseForge's games, by their slugs, with their classes, such as Minecraft's Mods
/// and Resource Packs, as their categories; a mod; and its files, the newest first.
/// </summary>
public sealed partial class CurseForgeSource : IModCatalog
{
    // A page of a mod's files, the most CurseForge gives.
    private const int FilesPageSize = 50;

    // Mods CurseForge took down: rejected and deleted.
    private const int Rejected = 5;
    private const int Deleted = 9;

    /// <summary>
    /// True: a mod's files are builds for different game versions and loaders, side by side, so one's update is only the
    /// newer build of its kind (<see cref="CurseForgeFiles"/>).
    /// </summary>
    public bool FilesSideBySide => true;

    /// <summary>
    /// False: CurseForge names its games by slugs, such as <c>minecraft</c>, which Nexus Mods' keys look like, so a key
    /// alone is never taken for CurseForge's, as for Thunderstore's: its games always need their site.
    /// </summary>
    public bool CouldBeGameKey(string key) => false;

    public async Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken)
        => [.. (await _games.AllAsync(cancellationToken).ConfigureAwait(false)).Select(game => GameInfoOf(game, []))];

    public async Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken)
    {
        CurseForgeGame found = await GameAsync(game, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CurseForgeCategory> classes = await _games.ClassesAsync(found.Id, cancellationToken).ConfigureAwait(false);
        return GameInfoOf(found, classes.Select(type => type.Name!));
    }

    public async Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (CurseForgeMod mod, CurseForgeGame game) = await ModAsync(modRef, cancellationToken).ConfigureAwait(false);
        return ModInfoOf(mod, game, await _games.ClassesAsync(game.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (CurseForgeMod mod, CurseForgeGame game) = await ModAsync(modRef, cancellationToken).ConfigureAwait(false);
        List<CurseForgeFile> files = [];
        for (int page = 0; ; page++)
        {
            if (page == _options.MaxFilePages)
            {
                LogFilesCut(_logger, mod.Id, files.Count);
                break;
            }

            CurseForgeAnswer<IReadOnlyList<CurseForgeFile>> answer = await _api
                .GetAsync<IReadOnlyList<CurseForgeFile>>(
                    string.Create(CultureInfo.InvariantCulture, $"v1/mods/{mod.Id}/files?index={page * FilesPageSize}&pageSize={FilesPageSize}"),
                    cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<CurseForgeFile> found = answer.Data ?? [];
            files.AddRange(found);
            if (found.Count < FilesPageSize || answer.Pagination is not { } pagination || files.Count >= pagination.TotalCount)
            {
                break;
            }
        }

        return CurseForgeFiles.Of(new ModRef(modRef.Game, Number(mod.Id)), PageOf(mod, game), files.DistinctBy(file => file.Id));
    }

    /// <summary>
    /// None: CurseForge keeps a changelog for each file, as a web page, which ModShelf doesn't read in 1.1.0.
    /// </summary>
    public Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ModId(modRef);
        return Task.FromResult<IReadOnlyList<ModChangelog>>([]);
    }

    private static Uri? WebLink(string? link)
        => Uri.TryCreate(link, UriKind.Absolute, out Uri? parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp) ? parsed : null;

    private GameInfo GameInfoOf(CurseForgeGame game, IEnumerable<string> classes)
        => new(
            new GameRef(CurseForgeId, game.Slug!.ToLowerInvariant()),
            game.Name!.Trim(),
            new Uri(_options.WebAddress, game.Slug),
            classes,
            WebLink(game.Assets?.CoverUrl) ?? WebLink(game.Assets?.TileUrl),
            WebLink(game.Assets?.IconUrl));

    // A mod's category is its class, such as Mods or Resource Packs: what a search by category asks for.
    private ModInfo ModInfoOf(CurseForgeMod mod, CurseForgeGame game, IReadOnlyList<CurseForgeCategory> classes)
        => new(
            new ModRef(new GameRef(CurseForgeId, game.Slug!.ToLowerInvariant()), Number(mod.Id)),
            string.IsNullOrWhiteSpace(mod.Name) ? Number(mod.Id) : mod.Name.Trim(),
            summary: mod.Summary,
            author: mod.Authors?.FirstOrDefault(author => !string.IsNullOrWhiteSpace(author.Name))?.Name?.Trim(),
            pageUrl: PageOf(mod, game),
            pictureUrl: WebLink(mod.Logo?.Url),
            updatedAt: mod.DateModified,
            isAvailable: mod.IsAvailable && mod.Status is not (Rejected or Deleted),
            downloads: Math.Max(0, mod.DownloadCount),
            thumbnailUrl: WebLink(mod.Logo?.ThumbnailUrl) ?? WebLink(mod.Logo?.Url),
            category: classes.FirstOrDefault(type => type.Id == mod.ClassId)?.Name?.Trim(),
            publishedAt: mod.DateReleased ?? mod.DateCreated);
}
