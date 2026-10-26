using ModShelf.Domain.Mods;

namespace ModShelf.Sources.Abstractions.Catalog;

/// <summary>
/// Describes the source's games and mods: what a game is called, a mod's details, its files and its changelog.
/// </summary>
/// <remarks>
/// Each method asks the site afresh, so callers ask only for what they show. Every method refuses a game or mod of
/// another source with <see cref="ArgumentException"/>, and reports a refused account as
/// <see cref="Accounts.AuthenticationFailedException"/> and anything else the site refuses, such as a mod that
/// doesn't exist (404), as <see cref="HttpRequestException"/>.
/// </remarks>
public interface IModCatalog : IModSource
{
    Task<GameInfo> GetGameAsync(GameRef game, CancellationToken cancellationToken);

    Task<ModInfo> GetModAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <returns>
    /// Every file the site lists for the mod, old and archived ones included, in no particular order. Each file's
    /// <see cref="ModFile.PageUrl"/>, when it has one, is a link the source's <see cref="Links.ILinkRecognizer"/> takes.
    /// </returns>
    Task<IReadOnlyList<ModFile>> GetFilesAsync(ModRef modRef, CancellationToken cancellationToken);

    /// <returns>The changes listed for each version, in the order the site gives them; empty when there are none.</returns>
    Task<IReadOnlyList<ModChangelog>> GetChangelogsAsync(ModRef modRef, CancellationToken cancellationToken);
}
