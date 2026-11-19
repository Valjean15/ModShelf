using ModShelf.Domain.Mods;

namespace ModShelf.Application.Requirements;

/// <summary>
/// Downloads what a mod needs with it (ADR 0102): the mods its site says it requires that the library lacks, each by its
/// newest main file, after the user has seen the list. The site's requirements aren't always right, so nothing is ever
/// downloaded unasked, and things off the site, such as a script extender, are only ever pointed to.
/// </summary>
public interface IRequirementDownloads
{
    /// <summary>
    /// What the mod needs, and what downloading it with its requirements would do with each. A mod the library has, or
    /// one downloading, is said to be; the others get their newest main file. With <paramref name="deeper"/>, the
    /// requirements of the mods to download are listed too, down to the third level; a mod listed once, the mod itself
    /// included, isn't listed again, so a cycle ends.
    /// </summary>
    /// <remarks>
    /// It asks the site once for the mod's requirements, once for each mod needed that the library lacks, for its files,
    /// and, deeper, once more for each of those, for their requirements. One that can't be read is said to be, with why.
    /// </remarks>
    /// <returns>The plan; one without requirements, not <see cref="RequirementsPlan.Listed"/>, for a site that lists none.</returns>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">The site needs a linked account, or refused it.</exception>
    /// <exception cref="HttpRequestException">The mod's requirements couldn't be read.</exception>
    Task<RequirementsPlan> PlanAsync(ModRef modRef, bool deeper, CancellationToken cancellationToken);

    /// <summary>
    /// What the mod a link is to needs, as <see cref="PlanAsync(ModRef, bool, CancellationToken)"/> plans it, the link
    /// recognized first, as Download recognizes it.
    /// </summary>
    /// <exception cref="Sources.Abstractions.Links.UnsupportedLinkException">No source knows the link.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <inheritdoc cref="PlanAsync(ModRef, bool, CancellationToken)" path="/exception"/>
    Task<RequirementsPlan> PlanAsync(Uri link, bool deeper, CancellationToken cancellationToken);

    /// <summary>
    /// Queues the file of each requirement given that's to be downloaded, one at a time, as Download does; one that
    /// can't be queued doesn't stop the others. Requirements in any other state are passed over.
    /// </summary>
    Task<RequirementDownloads> QueueAsync(IReadOnlyList<PlannedRequirement> chosen, CancellationToken cancellationToken);
}
