using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Lookup;

/// <summary>
/// Finds a game's files on the site by a hash of their content, as a mod manager identifies the archives it's given
/// (ADR 0101). A source implements it only when its site looks files up by hash, such as Nexus Mods by MD5.
/// </summary>
/// <remarks>
/// A site may want an account for it: a refused or missing one is <see cref="Accounts.AuthenticationFailedException"/>.
/// Anything else the site refuses is <see cref="HttpRequestException"/>, with the site's own words in its message.
/// </remarks>
public interface IFileLookup : IModSource
{
    /// <summary>
    /// The hash the site finds files by, such as <see cref="FileHashAlgorithm.Md5"/> on Nexus Mods.
    /// </summary>
    FileHashAlgorithm LookupAlgorithm { get; }

    /// <summary>
    /// The game's files on the site whose content has <paramref name="hash"/>, each with its mod.
    /// </summary>
    /// <returns>
    /// None when the site knows no such file; more than one when the same archive was uploaded more than once, such as
    /// to two mods.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The game isn't this source's, or the hash isn't a <see cref="LookupAlgorithm"/> one.
    /// </exception>
    Task<IReadOnlyList<FoundFile>> FindByHashAsync(GameRef game, FileHash hash, CancellationToken cancellationToken);
}
