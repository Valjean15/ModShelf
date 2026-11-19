using System.Globalization;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Links;
using ModShelf.Sources.CurseForge.Api;
using ModShelf.Sources.CurseForge.Links;

namespace ModShelf.Sources.CurseForge;

/// <summary>
/// The <see cref="ILinkRecognizer"/> half: a link to a mod on CurseForge's website names it by slugs, so CurseForge is
/// asked which mod has the slug, in the game's class the link names; a mod's link is its main file, a file's link that
/// file.
/// </summary>
public sealed partial class CurseForgeSource : ILinkRecognizer
{
    /// <exception cref="ArgumentException">The link is CurseForge's, but to a game or a class CurseForge hasn't, or to no one file.</exception>
    /// <exception cref="HttpRequestException">CurseForge has no mod at the link, or couldn't be asked.</exception>
    public async Task<RecognizedLink?> RecognizeAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (CurseForgeLink.Parse(link) is not { } parsed)
        {
            return null;
        }

        CurseForgeGame game = await _games.BySlugAsync(parsed.Game, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"CurseForge has no game called {parsed.Game}.", nameof(link));
        CurseForgeCategory type = (await _games.ClassesAsync(game.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(listed => string.Equals(listed.Slug, parsed.Class, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{game.Name} has no class of mods called {parsed.Class} on CurseForge.", nameof(link));
        CurseForgeAnswer<IReadOnlyList<CurseForgeMod>> found = await _api
            .GetAsync<IReadOnlyList<CurseForgeMod>>(
                string.Create(CultureInfo.InvariantCulture, $"v1/mods/search?gameId={game.Id}&classId={type.Id}&slug={Uri.EscapeDataString(parsed.Mod)}"),
                cancellationToken)
            .ConfigureAwait(false);
        CurseForgeMod mod = (found.Data ?? []).FirstOrDefault(listed => string.Equals(listed.Slug, parsed.Mod, StringComparison.OrdinalIgnoreCase))
            ?? throw new HttpRequestException($"CurseForge has no mod at {link}.", null, System.Net.HttpStatusCode.NotFound);
        long file = parsed.File ?? (mod.MainFileId > 0
            ? mod.MainFileId
            : throw new HttpRequestException($"{mod.Name} has no main file on CurseForge.", null, System.Net.HttpStatusCode.NotFound));
        return new RecognizedLink(new ModFileRef(new ModRef(new GameRef(CurseForgeId, game.Slug!.ToLowerInvariant()), Number(mod.Id)), Number(file)));
    }
}
