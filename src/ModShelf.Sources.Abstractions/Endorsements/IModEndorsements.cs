using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Endorsements;

/// <summary>
/// The linked account's endorsements on the site, such as Nexus Mods': read, and changed one mod at a time. A source
/// implements it only when its site lets an app endorse. The site names endorsing in its <see cref="IModSource.Words"/>:
/// <see cref="Common.SiteWords.Approve"/> and <see cref="Common.SiteWords.Unapprove"/>.
/// </summary>
/// <remarks>
/// Every method acts on the linked account, so each needs one: a refused or missing one is
/// <see cref="Accounts.AuthenticationFailedException"/>. Anything else the site refuses, such as endorsing a mod the
/// account never downloaded, is <see cref="HttpRequestException"/>, with the site's own words in its message.
/// </remarks>
public interface IModEndorsements : IModSource
{
    /// <returns>What the account said of each mod it endorsed or chose not to, of every game; a mod it said nothing of isn't listed.</returns>
    Task<IReadOnlyDictionary<ModRef, Endorsement>> GetEndorsementsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Endorses the mod, or chooses not to, in place of what the account said before.
    /// </summary>
    /// <param name="version">The mod's version, as the site names it, when the site wants it; null for the site's own.</param>
    /// <exception cref="ArgumentException">The mod isn't this source's.</exception>
    Task EndorseAsync(ModRef modRef, Endorsement endorsement, string? version, CancellationToken cancellationToken);
}
