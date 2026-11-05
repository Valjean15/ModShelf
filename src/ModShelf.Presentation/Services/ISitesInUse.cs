namespace ModShelf.Presentation.Services;

/// <summary>
/// Whether the screens show each game's, mod's and update's site: once two sites are in use, those the library has mods
/// of and those with an account linked (ADR 0069). A screen asks each time it's shown. Direct links aren't a site: the
/// library never holds their files, and they have no accounts.
/// </summary>
public interface ISitesInUse
{
    /// <returns>
    /// Whether two sites or more are in use; false when the library or the accounts can't be read, which is logged, since
    /// a site shown too seldom misleads less than one shown wrongly.
    /// </returns>
    Task<bool> AreSeveralAsync(CancellationToken cancellationToken);
}
