using System.Collections.Concurrent;
using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;

namespace ModShelf.Cli.Tests.Fakes;

/// <summary>
/// The account's endorsements, kept in memory: at first, none. Mod 2, which is hidden, is refused, as Nexus Mods
/// refuses a mod the account didn't download, and mod 503 fails.
/// </summary>
internal sealed partial class FakeCatalogSite : IModEndorsements
{
    private readonly ConcurrentDictionary<ModRef, Endorsement> _endorsed = new();

    /// <summary>
    /// What the account said, by mod.
    /// </summary>
    public IReadOnlyDictionary<ModRef, Endorsement> Endorsed => _endorsed;

    public Task<IReadOnlyDictionary<ModRef, Endorsement>> GetEndorsementsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<ModRef, Endorsement>>(new Dictionary<ModRef, Endorsement>(_endorsed));

    public Task EndorseAsync(ModRef modRef, Endorsement endorsement, string? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(modRef);
        if (modRef.Game.Source != SourceId)
        {
            throw new ArgumentException("That's another site's mod.", nameof(modRef));
        }

        if (modRef.Id == "2")
        {
            return Task.FromException(new HttpRequestException("You need to download the mod before endorsing it.", null, HttpStatusCode.Forbidden));
        }

        if (modRef.Id == "503")
        {
            return Task.FromException(new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable));
        }

        _endorsed[modRef] = endorsement;
        return Task.CompletedTask;
    }
}
