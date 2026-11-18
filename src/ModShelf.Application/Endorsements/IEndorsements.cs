using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Application.Endorsements;

/// <summary>
/// The linked accounts' endorsements on the sites that let ModShelf endorse, such as Nexus Mods (ADR 0100): read by
/// site, and changed a mod at a time, by reference or by link. Nothing is kept: each call asks the site.
/// </summary>
public interface IEndorsements
{
    /// <summary>
    /// Whether ModShelf can endorse mods on the source, with an account linked there.
    /// </summary>
    bool CanEndorse(SourceId source);

    /// <returns>What the account linked to <paramref name="source"/> said of each mod it endorsed or abstained from.</returns>
    /// <exception cref="ArgumentException">The source isn't registered, or can't endorse.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused.</exception>
    Task<IReadOnlyDictionary<ModRef, Endorsement>> ListAsync(SourceId source, CancellationToken cancellationToken);

    /// <summary>
    /// Endorses the mod, or abstains from it.
    /// </summary>
    /// <param name="version">The mod's version endorsed, when known, such as its saved details'; null for the site's own.</param>
    /// <exception cref="ArgumentException">The mod's source isn't registered, or can't endorse.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused, as for a mod the account didn't download; in its words.</exception>
    Task EndorseAsync(ModRef modRef, Endorsement endorsement, string? version, CancellationToken cancellationToken);

    /// <summary>
    /// Endorses the mod the link is to, or to one of whose files, or abstains from it, with its site's version.
    /// </summary>
    /// <returns>The mod.</returns>
    /// <exception cref="Downloads.UnsupportedLinkException">No source knows the link, or its site can't endorse.</exception>
    /// <exception cref="ArgumentException">The link is malformed, or is the site's but not to a mod or a file.</exception>
    /// <exception cref="Sources.Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the site refused it.</exception>
    /// <exception cref="HttpRequestException">The site couldn't be reached, or refused; in its words.</exception>
    Task<ModRef> EndorseAsync(Uri link, Endorsement endorsement, CancellationToken cancellationToken);
}
