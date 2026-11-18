using ModShelf.Application.Downloads;
using ModShelf.Application.Sources;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Application.Endorsements;

/// <summary>
/// Finds the source a mod or a link belongs to, as <see cref="Watchlist.IWatchlists"/> does, and asks its
/// <see cref="IModEndorsements"/>.
/// </summary>
internal sealed class SiteEndorsements(ISourceRegistry sources) : IEndorsements
{
    public bool CanEndorse(SourceId source) => source is not null && sources.Find<IModEndorsements>(source) is not null;

    public Task<IReadOnlyDictionary<ModRef, Endorsement>> ListAsync(SourceId source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        return sources.Require<IModEndorsements>(source, "can't endorse mods", nameof(source)).GetEndorsementsAsync(cancellationToken);
    }

    public Task EndorseAsync(ModRef modRef, Endorsement endorsement, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);

        return sources.Require<IModEndorsements>(modRef.Game.Source, "can't endorse mods", nameof(modRef))
            .EndorseAsync(modRef, endorsement, version, cancellationToken);
    }

    public async Task<ModRef> EndorseAsync(Uri link, Endorsement endorsement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        ModRef mod = (await sources.RecognizeLinkAsync(link, cancellationToken).ConfigureAwait(false)).File.Mod;
        IModEndorsements site = sources.Find<IModEndorsements>(mod.Game.Source)
            ?? throw new UnsupportedLinkException("This link isn't to a mod on a site where ModShelf can endorse mods.");
        await site.EndorseAsync(mod, endorsement, null, cancellationToken).ConfigureAwait(false);
        return mod;
    }
}
