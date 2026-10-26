using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Catalog;
using ModShelf.Sources.Abstractions.Links;

namespace ModShelf.Application.Catalog;

/// <summary>
/// Finds the source a link belongs to, as <see cref="ILinkDownloads"/> does, and asks its <see cref="IModCatalog"/>
/// about the mod, all four questions at once.
/// </summary>
internal sealed class ModLookup : IModLookup
{
    private readonly ISourceRegistry _sources;

    public ModLookup(ISourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources;
    }

    public async Task<ModPage> LookUpAsync(Uri link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri)
        {
            throw new ArgumentException("The link must be absolute.", nameof(link));
        }

        RecognizedLink recognized;
        try
        {
            recognized = await _sources.RecognizeAsync(link, cancellationToken).ConfigureAwait(false);
        }
        catch (UnsupportedLinkException exception)
        {
            throw new UnsupportedLinkException("No source knows this link.", exception);
        }

        ModRef mod = recognized.File.Mod;
        IModCatalog catalog = _sources.Find<IModCatalog>(mod.Game.Source)
            ?? throw new UnsupportedLinkException("This link isn't to a mod on a site ModShelf can look mods up on.");

        Task<GameInfo> game = catalog.GetGameAsync(mod.Game, cancellationToken);
        Task<ModInfo> info = catalog.GetModAsync(mod, cancellationToken);
        Task<IReadOnlyList<ModFile>> files = catalog.GetFilesAsync(mod, cancellationToken);
        Task<IReadOnlyList<ModChangelog>> changelogs = catalog.GetChangelogsAsync(mod, cancellationToken);
        await Task.WhenAll(game, info, files, changelogs).ConfigureAwait(false);

        return new ModPage(
            await game.ConfigureAwait(false),
            await info.ConfigureAwait(false),
            await files.ConfigureAwait(false),
            await changelogs.ConfigureAwait(false),
            recognized.File);
    }
}
