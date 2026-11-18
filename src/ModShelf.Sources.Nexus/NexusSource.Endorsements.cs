using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Endorsements;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IModEndorsements"/> half: the account's endorsements, through the v1 API, as Nexus Mods' own app reads
/// and changes them (ADR 0100).
/// </summary>
public sealed partial class NexusSource : IModEndorsements
{
    private const string EndorsementsPath = "v1/user/endorsements.json";

    /// <returns>Each mod the account endorsed or abstained from, of every game; one listed twice, as it says last.</returns>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyDictionary<ModRef, Endorsement>> GetEndorsementsAsync(CancellationToken cancellationToken)
    {
        NexusEndorsement?[] listed = await _api.GetAsync<NexusEndorsement?[]>(EndorsementsPath, cancellationToken).ConfigureAwait(false);
        Dictionary<ModRef, Endorsement> said = [];
        foreach (NexusEndorsement? endorsement in listed)
        {
            // An entry without a game or ID Nexus Mods gives, or a status it doesn't document, says nothing.
            if (endorsement is not null
                && NexusLink.FromApi(endorsement.DomainName, endorsement.ModId) is { } mod
                && EndorsementOf(endorsement.Status) is { } state)
            {
                said[ToModRef(mod)] = state;
            }
        }

        return said;
    }

    /// <summary>
    /// A POST to the mod's <c>endorse.json</c> or <c>abstain.json</c>, with the version endorsed in a form; without one
    /// given, the mod's own, read first.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't Nexus Mods', or its game or ID aren't ones Nexus Mods gives.</exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">
    /// The API couldn't be reached, or refused, as it does a mod the account didn't download, or its own; with its words.
    /// </exception>
    public async Task EndorseAsync(ModRef modRef, Endorsement endorsement, string? version, CancellationToken cancellationToken)
    {
        NexusLink mod = NexusLink.ForMod(modRef);
        if (!Enum.IsDefined(endorsement))
        {
            throw new ArgumentOutOfRangeException(nameof(endorsement), endorsement, "An endorsement is Endorsed or Abstained.");
        }

        string said = string.IsNullOrWhiteSpace(version)
            ? (await _api.GetAsync<NexusMod>($"{mod.ModApiPath}.json", cancellationToken).ConfigureAwait(false)).Version ?? string.Empty
            : version.Trim();
        string action = endorsement == Endorsement.Endorsed ? "endorse" : "abstain";
        HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{mod.ModApiPath}/{action}.json", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("version", said)]),
        };
        await _api.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The endorsement a status says; null for one Nexus Mods doesn't document, such as <c>Undecided</c>.
    /// </summary>
    private static Endorsement? EndorsementOf(string? status) => status?.ToUpperInvariant() switch
    {
        "ENDORSED" => Endorsement.Endorsed,
        "ABSTAINED" => Endorsement.Abstained,
        _ => null,
    };
}
