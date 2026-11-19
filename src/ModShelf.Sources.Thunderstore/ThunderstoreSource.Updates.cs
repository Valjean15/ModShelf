using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Updates;
using ModShelf.Sources.Thunderstore.Catalog;
using ModShelf.Sources.Thunderstore.Links;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// The <see cref="IUpdateFeed"/> half: the community's packages updated since a time, from its listing, which holds every
/// package with when it was last updated and when its newest version was uploaded.
/// </summary>
public sealed partial class ThunderstoreSource : IUpdateFeed
{
    // The listing holds every package, so the feed could reach any time back; a year keeps the question sensible.
    private static readonly TimeSpan UpdateReach = TimeSpan.FromDays(365);

    // A since the caller read from its own clock a moment before this source reads it is still taken.
    private static readonly TimeSpan ReachGrace = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A year.
    /// </summary>
    public TimeSpan Reach => UpdateReach;

    /// <returns>The community's packages updated since <paramref name="since"/>, deprecated ones too.</returns>
    /// <exception cref="ArgumentException">The game isn't Thunderstore's.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="since"/> is more than a year ago.</exception>
    /// <exception cref="HttpRequestException">The community's listing couldn't be read.</exception>
    public async Task<IReadOnlyList<ModUpdate>> GetUpdatesAsync(GameRef game, DateTimeOffset since, CancellationToken cancellationToken)
    {
        string community = Community(game);
        if (_time.GetUtcNow() - since > UpdateReach + ReachGrace)
        {
            throw new ArgumentOutOfRangeException(nameof(since), "ModShelf reads Thunderstore's updates of the last year only.");
        }

        Listing listing = await _listings.GetAsync(community, cancellationToken).ConfigureAwait(false);
        return
        [
            .. listing.Packages
                .Where(package => package.UpdatedAt >= since)
                .Select(package => new ModUpdate(
                    ModOf(community, package.Name),
                    package.UpdatedAt,
                    package.Newest.UploadedAt,
                    ThunderstoreLink.Page(community, package.Name))),
        ];
    }
}
