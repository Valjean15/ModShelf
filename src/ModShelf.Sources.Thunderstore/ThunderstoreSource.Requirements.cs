using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Thunderstore.Catalog;
using ModShelf.Sources.Thunderstore.Links;

namespace ModShelf.Sources.Thunderstore;

/// <summary>
/// The <see cref="IModRequirements"/> half: the packages the newest version depends on, as its manifest names them,
/// <c>Team-Name-1.2.3</c>, each a mod of the same community, named with its team. One the community doesn't list, such as another game's
/// BepInEx pack, is pointed to as a thing off the site, by its page, since ModShelf doesn't put another game's package
/// in this one's library (ADR 0103).
/// </summary>
public sealed partial class ThunderstoreSource : IModRequirements
{
    /// <returns>The newest version's dependencies, in its manifest's order; empty when it has none.</returns>
    /// <exception cref="ArgumentException">The mod isn't Thunderstore's.</exception>
    /// <exception cref="HttpRequestException">The community doesn't list the package (404), or its listing couldn't be read.</exception>
    public async Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        ListedPackage package = await ListedAsync(modRef, cancellationToken).ConfigureAwait(false);
        string community = modRef.Game.Key;
        Listing listing = await _listings.GetAsync(community, cancellationToken).ConfigureAwait(false);
        List<ModRequirement> requirements = [];
        foreach (string dependency in package.Dependencies)
        {
            if (PackageName.ParseDependency(dependency) is not ({ } needed, var version))
            {
                LogOddDependency(_logger, package.Name.FullName, community);
                continue;
            }

            string notes = $"Version {version} or later";
            requirements.Add(listing.Find(needed.FullName) is { } listed
                ? new ModRequirement(Named(listed.Name), ModOf(community, listed.Name), ThunderstoreLink.Page(community, listed.Name), notes)
                : new ModRequirement(
                    Named(needed),
                    pageUrl: ThunderstoreLink.PackagePage(needed),
                    notes: $"{notes}. Listed on Thunderstore, but not for this game"));
        }

        return requirements;
    }

    /// <summary>
    /// The package's name with its team's, as Thunderstore lists a package's dependencies: two teams' packages can share a
    /// name, such as ENVY.
    /// </summary>
    private static string Named(PackageName package) => $"{package.DisplayName} by {package.Team}";
}
