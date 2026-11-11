using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;
using ModShelf.Sources.ModIo.Links;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="ILinkRecognizer"/> half: which file a link the user gives points to. A mod's page names its game and
/// itself by the names in their links, which are looked up; the API's links name them by their IDs.
/// </summary>
public sealed partial class ModIoSource : ILinkRecognizer
{
    /// <summary>
    /// Recognizes links to a mod's page, and the API's links to a mod or one of its files, such as those ModShelf lists
    /// files by. A link to the mod as a whole means its live file for Windows.
    /// </summary>
    /// <returns>The file, or null when the link isn't mod.io's.</returns>
    /// <exception cref="ArgumentException">
    /// The link is mod.io's, but to something other than a mod or a file of one, or to a game or a mod mod.io doesn't
    /// have, or to a mod without a file for Windows.
    /// </exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No key or token is linked, or mod.io refused it.</exception>
    /// <exception cref="HttpRequestException">mod.io couldn't be reached, or refused; with 403 when the game is closed to apps.</exception>
    public async Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        if (ModIoLink.Read(link) is not { } found)
        {
            return null;
        }

        if (found is { GameId: { } gameId, ModId: { } modId, FileId: { } fileId })
        {
            return new RecognizedLink(new ModFileRef(new ModRef(GameOf(gameId), Number(modId)), Number(fileId)));
        }

        ModIoMod mod = found is { GameId: { } game, ModId: { } byId }
            ? await ModAsync(game, byId, cancellationToken).ConfigureAwait(false)
            : await ModNamedAsync(found.GameName!, found.ModName!, cancellationToken).ConfigureAwait(false);
        long live = ModIoMods.LiveFile(mod)
            ?? throw new ArgumentException("The mod.io mod has no file for Windows to download.", nameof(link));
        // A mod with a file of its own gives its live file.
        string? fileName = mod.Modfile is { Id: > 0 } file ? ModIoFiles.FileName(file) : null;
        return new RecognizedLink(new ModFileRef(new ModRef(GameOf(mod.GameId), Number(mod.Id)), Number(live)), fileName);
    }

    /// <summary>
    /// The mod of that name in links, of the game of that name, each as mod.io finds it by the name: the mod's can change.
    /// </summary>
    /// <exception cref="ArgumentException">mod.io has no such game, or no such mod of it.</exception>
    private async Task<ModIoMod> ModNamedAsync(string gameName, string modName, CancellationToken cancellationToken)
    {
        if (!_gamesByName.TryGetValue(gameName, out long gameId))
        {
            ModIoPage<ModIoGame> games = await _api.GetAsync<ModIoPage<ModIoGame>>(Api($"games?name_id={Escaped(gameName)}"), cancellationToken)
                .ConfigureAwait(false);
            gameId = (games.Data ?? []).FirstOrDefault(game => game is { Id: > 0 } && string.Equals(game.NameId, gameName, StringComparison.OrdinalIgnoreCase))?.Id
                ?? throw new ArgumentException($"mod.io has no game '{gameName}'.", nameof(gameName));
            _gamesByName[gameName] = gameId;
        }

        ModIoPage<ModIoMod> mods = await _api.GetAsync<ModIoPage<ModIoMod>>(
                GameApi(gameId, $"games/{Number(gameId)}/mods?name_id={Escaped(modName)}"),
                cancellationToken)
            .ConfigureAwait(false);
        return (mods.Data ?? []).FirstOrDefault(mod => mod is { Id: > 0 } && string.Equals(mod.NameId, modName, StringComparison.OrdinalIgnoreCase)) is { } named
            ? named with { GameId = named.GameId > 0 ? named.GameId : gameId }
            : throw new ArgumentException($"mod.io has no mod '{modName}' of that game: its author may have renamed it.", nameof(modName));
    }
}
