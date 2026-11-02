using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// Says what a mod needs, as its author listed it on the site: other mods, and things off the site such as a script
/// extender. A source implements it only when its site lists requirements. ModShelf only offers them; it never installs
/// anything.
/// </summary>
/// <remarks>
/// It refuses a mod of another source with <see cref="ArgumentException"/>, and reports a refused account as
/// <see cref="Accounts.AuthenticationFailedException"/> and anything else the site refuses as
/// <see cref="HttpRequestException"/>.
/// </remarks>
public interface IModRequirements : IModSource
{
    /// <returns>The mod's requirements, in the order the site lists them; empty when it lists none.</returns>
    Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken);
}
