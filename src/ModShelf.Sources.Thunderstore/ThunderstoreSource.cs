using System.Collections.Concurrent;
using System.Net;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Thunderstore.Api;
using ModShelf.Sources.Thunderstore.Catalog;
using ModShelf.Sources.Thunderstore.Links;
using ModShelf.Sources.Thunderstore.Resources;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// Thunderstore (<c>thunderstore.io</c>), whose communities are its games and whose packages are their mods, each version
/// a file. It needs no account. Games come from <c>api/cyberstorm/community/</c>, read once a run; each community's
/// packages from its listing, kept in memory for an hour (<see cref="ThunderstoreListings"/>). Games are named by their
/// community's identifier, mods by their package's full name, <c>Team-Name</c>, and files by their version number
/// (ADR 0103). Every request goes through the client the app sets up for the source.
/// </summary>
public sealed partial class ThunderstoreSource : IModSource
{
    /// <summary>
    /// The source's ID. It's stored with every job, so it never changes.
    /// </summary>
    internal static readonly SourceId ThunderstoreId = new("thunderstore");

    private readonly ThunderstoreApiClient _api;
    private readonly ThunderstoreListings _listings;
    private readonly TimeProvider _time;
    private readonly ILogger<ThunderstoreSource> _logger;

    // The communities, read once a run by whoever asks first; a read that fails is tried again by the next caller.
    private readonly Lock _communitiesGate = new();
    private Task<IReadOnlyList<ThunderstoreCommunity>>? _communities;

    // Each community's categories, by its identifier, read once a run.
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<string>>> _categories = new(StringComparer.Ordinal);

    internal ThunderstoreSource(ThunderstoreApiClient api, ThunderstoreListings listings, TimeProvider time, ILogger<ThunderstoreSource> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _listings = listings;
        _time = time;
        _logger = logger;
    }

    public SourceId Id => ThunderstoreId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// Thunderstore counts likes. Liking takes a Thunderstore account, which ModShelf can't link, so it isn't offered.
    /// </summary>
    public SiteWords Words { get; } = new()
    {
        Approval = Strings.WordApproval,
        Approvals = Strings.WordApprovals,
    };

    /// <summary>
    /// The game's community, as Thunderstore names it.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't Thunderstore's.</exception>
    private static string Community(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Source == ThunderstoreId && IsCommunity(game.Key)
            ? game.Key
            : throw new ArgumentException($"The game '{game.Key}' of {game.Source} isn't Thunderstore's.", nameof(game));
    }

    /// <summary>
    /// The mod's package.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't Thunderstore's.</exception>
    private static PackageName Package(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        Community(mod.Game);
        return PackageName.Parse(mod.Id)
            ?? throw new ArgumentException($"The mod '{mod.Id}' isn't Thunderstore's: its mods are named Team-Name.", nameof(mod));
    }

    /// <summary>
    /// The file's version.
    /// </summary>
    /// <exception cref="ArgumentException">The file isn't Thunderstore's.</exception>
    private static string Version(ModFileRef file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Package(file.Mod);
        return PackageName.IsVersion(file.Id)
            ? file.Id
            : throw new ArgumentException($"The file '{file.Id}' isn't Thunderstore's: its files are versions, such as 1.0.0.", nameof(file));
    }

    /// <summary>
    /// Whether <paramref name="key"/> is shaped as a community's identifier: lowercase letters and digits, a letter at
    /// least, between hyphens. A number alone is GameBanana's or mod.io's.
    /// </summary>
    private static bool IsCommunity(string? key)
        => !string.IsNullOrEmpty(key)
            && key[0] != '-'
            && key[^1] != '-'
            && !key.Contains("--", StringComparison.Ordinal)
            && key.Any(char.IsAsciiLetterLower)
            && key.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    private static GameRef GameOf(string community) => new(ThunderstoreId, community);

    private static ModRef ModOf(string community, PackageName package) => new(GameOf(community), package.FullName);

    /// <summary>
    /// The package <paramref name="mod"/> is, as its community lists it now.
    /// </summary>
    /// <exception cref="HttpRequestException">The listing couldn't be read, or doesn't list the package (404).</exception>
    private async Task<ListedPackage> ListedAsync(ModRef mod, CancellationToken cancellationToken)
    {
        PackageName package = Package(mod);
        Listing listing = await _listings.GetAsync(mod.Game.Key, cancellationToken).ConfigureAwait(false);
        return listing.Find(package.FullName)
            ?? throw new HttpRequestException(
                $"Thunderstore doesn't list {package.FullName} in {mod.Game.Key}: it may have been removed, or be another game's.",
                null,
                HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The package as ModShelf describes a mod, in its first category, or in <paramref name="category"/> when given.
    /// </summary>
    private static ModInfo Describe(string community, ListedPackage package, string? category = null)
        => new(
            ModOf(community, package.Name),
            package.Name.DisplayName,
            summary: package.Description,
            author: package.Name.Team,
            version: package.Newest.Number,
            pageUrl: ThunderstoreLink.Page(community, package.Name),
            pictureUrl: package.Icon,
            updatedAt: package.UpdatedAt,
            hasAdultContent: package.HasAdultContent,
            downloads: package.Downloads,
            endorsements: package.Likes,
            thumbnailUrl: package.Icon,
            category: category ?? (package.Categories.Count > 0 ? package.Categories[0] : null),
            size: package.Newest.Size,
            publishedAt: package.CreatedAt);
}
