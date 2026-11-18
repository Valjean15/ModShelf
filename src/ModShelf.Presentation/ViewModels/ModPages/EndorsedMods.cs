using ModShelf.Application.Endorsements;
using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Presentation.ViewModels.ModPages;

/// <summary>
/// What the linked accounts said of mods on the sites that let ModShelf endorse (ADR 0100), one for the app, so Browse
/// and the Library agree: each site's list is read once, when first needed, and kept up to date with what ModShelf
/// endorses since.
/// </summary>
public sealed class EndorsedMods
{
    private readonly IEndorsements _endorsements;
    private readonly Lock _gate = new();
    private readonly Dictionary<SourceId, Dictionary<ModRef, Endorsement>> _bySource = [];

    public EndorsedMods(IEndorsements endorsements)
    {
        ArgumentNullException.ThrowIfNull(endorsements);

        _endorsements = endorsements;
    }

    /// <summary>
    /// Whether ModShelf can endorse mods on the site.
    /// </summary>
    public bool CanEndorse(SourceId source) => _endorsements.CanEndorse(source);

    /// <summary>
    /// Whether the site's list has been read, so <see cref="IsEndorsed"/> knows.
    /// </summary>
    public bool Knows(SourceId source)
    {
        lock (_gate)
        {
            return _bySource.ContainsKey(source);
        }
    }

    /// <summary>
    /// Whether the account endorsed the mod; null while its site's list hasn't been read.
    /// </summary>
    public bool? IsEndorsed(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        lock (_gate)
        {
            return _bySource.TryGetValue(mod.Game.Source, out Dictionary<ModRef, Endorsement>? said)
                ? said.GetValueOrDefault(mod) == Endorsement.Endorsed
                : null;
        }
    }

    /// <summary>
    /// Reads the site's list afresh, and keeps it.
    /// </summary>
    /// <inheritdoc cref="IEndorsements.ListAsync" path="/exception"/>
    public async Task ReadAsync(SourceId source, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<ModRef, Endorsement> said = await _endorsements.ListAsync(source, cancellationToken);
        lock (_gate)
        {
            _bySource[source] = new Dictionary<ModRef, Endorsement>(said);
        }
    }

    /// <summary>
    /// Endorses the mod on its site, or abstains from it.
    /// </summary>
    /// <param name="version">The mod's version, when known; null for its site's own.</param>
    /// <inheritdoc cref="IEndorsements.EndorseAsync(ModRef, Endorsement, string?, CancellationToken)" path="/exception"/>
    public async Task SetAsync(ModRef mod, Endorsement endorsement, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mod);

        await _endorsements.EndorseAsync(mod, endorsement, version, cancellationToken);
        lock (_gate)
        {
            if (_bySource.TryGetValue(mod.Game.Source, out Dictionary<ModRef, Endorsement>? said))
            {
                said[mod] = endorsement;
            }
        }
    }
}
