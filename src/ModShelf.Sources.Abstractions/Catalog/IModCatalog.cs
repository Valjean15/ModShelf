using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// Describes the source's games and mods: which games it has and what each is called, a mod's details, its files and
/// its changelog.
/// </summary>
/// <remarks>
/// Each method asks the site afresh, so callers ask only for what they show. Every method refuses a game or mod of
/// another source with <see cref="ArgumentException"/>, and reports a refused account as
/// <see cref="Accounts.AuthenticationFailedException"/> and anything else the site refuses, such as a mod that
/// doesn't exist (404), as <see cref="HttpRequestException"/>.
/// </remarks>
public interface IModCatalog : IModSource
{
    /// <returns>
    /// Every game the site has mods for, each once, in no particular order: a list for the user to pick a game from.
    /// </returns>
    Task<IReadOnlyList<GameInfo>> GetGamesAsync(CancellationToken cancellationToken);

    Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken);

    Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <returns>
    /// Every file the site lists for the mod, old and archived ones included, in no particular order. Each file's
    /// <see cref="ModFile.PageUrl"/>, when it has one, is a link the source's <see cref="Links.ILinkRecognizer"/> takes.
    /// </returns>
    Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <returns>The changes listed for each version, in the order the site gives them; empty when there are none.</returns>
    Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the site lists a mod's files side by side, without saying which is the main one or which replaced which,
    /// such as variants or add-ons uploaded at different times. ModShelf then never takes one for another's update by
    /// when they were uploaded: a file's update comes only from its chain (<see cref="ModFile.ReplacedBy"/>), and the
    /// main files uploaded since a mod was checked are new files. False unless the source says so.
    /// </summary>
    bool FilesSideBySide => false;
}
