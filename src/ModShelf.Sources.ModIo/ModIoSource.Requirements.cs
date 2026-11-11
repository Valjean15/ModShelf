using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.ModIo.Api;
using ModShelf.Sources.ModIo.Catalog;

namespace ModShelf.Sources.ModIo;

/// <summary>
/// The <see cref="IModRequirements"/> half: the mods the mod depends on, as its author listed them on mod.io.
/// </summary>
public sealed partial class ModIoSource : IModRequirements
{
    /// <returns>The mods it depends on, in mod.io's order, each with its game and its page; not what they depend on.</returns>
    /// <exception cref="ArgumentException">The mod isn't mod.io's.</exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No key or token is linked, or mod.io refused it.</exception>
    /// <exception cref="HttpRequestException">
    /// mod.io couldn't be reached, or refused; with 403 when the game is closed to apps, 404 when the mod doesn't exist.
    /// </exception>
    public async Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (long game, long mod) = ModId(modRef);
        ModIoPage<ModIoMod> found = await _api.GetAsync<ModIoPage<ModIoMod>>(
                GameApi(game, $"games/{Number(game)}/mods/{Number(mod)}/dependencies"),
                cancellationToken)
            .ConfigureAwait(false);
        return
        [
            .. (found.Data ?? [])
                .Where(needed => needed is { Id: > 0 })
                .Select(needed => new ModRequirement(
                    string.IsNullOrWhiteSpace(needed.Name) ? $"Mod {Number(needed.Id)}" : needed.Name,
                    new ModRef(GameOf(needed.GameId > 0 ? needed.GameId : game), Number(needed.Id)),
                    ModIoMods.WebLink(needed.ProfileUrl))),
        ];
    }
}
