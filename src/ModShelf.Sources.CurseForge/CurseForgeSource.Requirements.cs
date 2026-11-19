using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.CurseForge.Api;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// The <see cref="IModRequirements"/> half: the mods the mod's main file needs, as its author listed them on
/// CurseForge, the file read whole, and each mod named, read together in one request.
/// </summary>
public sealed partial class CurseForgeSource : IModRequirements
{
    // What a file's author says another mod is to it: needed.
    private const int Required = 3;

    public async Task<IReadOnlyList<ModRequirement>> GetRequirementsAsync(ModRef modRef, CancellationToken cancellationToken)
    {
        (CurseForgeMod mod, CurseForgeGame game) = await ModAsync(modRef, cancellationToken).ConfigureAwait(false);
        CurseForgeFile? main = mod.MainFileId > 0
            ? (await _api.GetAsync<CurseForgeFile>($"v1/mods/{Number(mod.Id)}/files/{Number(mod.MainFileId)}", cancellationToken).ConfigureAwait(false)).Data
            : null;

        long[] needed = [.. (main?.Dependencies ?? []).Where(dependency => dependency.RelationType == Required && dependency.ModId > 0 && dependency.ModId != mod.Id)
            .Select(dependency => dependency.ModId).Distinct()];
        if (needed.Length == 0)
        {
            return [];
        }

        IReadOnlyList<CurseForgeMod> named = (await _api.PostAsync<IReadOnlyList<CurseForgeMod>>("v1/mods", new CurseForgeModsRequest(needed), cancellationToken).ConfigureAwait(false)).Data ?? [];
        string gameKey = game.Slug!.ToLowerInvariant();
        return
        [
            .. needed.Select(id => named.FirstOrDefault(found => found.Id == id) is { } found
                ? new ModRequirement(string.IsNullOrWhiteSpace(found.Name) ? Number(id) : found.Name.Trim(), new ModRef(new GameRef(CurseForgeId, gameKey), Number(id)), PageOf(found, game))
                : new ModRequirement($"CurseForge mod {Number(id)}", new ModRef(new GameRef(CurseForgeId, gameKey), Number(id)))),
        ];
    }
}
