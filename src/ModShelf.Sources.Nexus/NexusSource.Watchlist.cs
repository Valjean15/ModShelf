using System.Net;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Watchlist;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Links;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// The <see cref="IWatchlistSync"/> half: the account's tracked mods, through the v1 API.
/// </summary>
public sealed partial class NexusSource : IWatchlistSync
{
    private const string TrackedModsPath = "v1/user/tracked_mods.json";

    /// <returns>Every mod the account tracks, of every game, each once.</returns>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused.</exception>
    public async Task<IReadOnlyList<ModRef>> GetWatchedAsync(CancellationToken cancellationToken)
    {
        NexusTrackedMod[] tracked = await _api.GetAsync<NexusTrackedMod[]>(TrackedModsPath, cancellationToken).ConfigureAwait(false);

        // An entry without a game or ID Nexus Mods gives can't be a mod to look up.
        return
        [
            .. tracked
                .Select(mod => mod is null ? null : NexusLink.FromApi(mod.DomainName, mod.ModId))
                .OfType<NexusLink>()
                .Select(ToModRef)
                .Distinct(),
        ];
    }

    /// <summary>
    /// Tracks the mod: a form POST to <c>tracked_mods.json</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't Nexus Mods', or its game or ID aren't ones Nexus Mods gives.</exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused; with 404 when there's no such mod.</exception>
    public Task WatchAsync(ModRef modRef, CancellationToken cancellationToken)
        => SetTrackedAsync(NexusLink.ForMod(modRef), track: true, cancellationToken);

    /// <summary>
    /// Stops tracking the mod: a form DELETE to <c>tracked_mods.json</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't Nexus Mods', or its game or ID aren't ones Nexus Mods gives.</exception>
    /// <exception cref="Abstractions.Accounts.AuthenticationFailedException">No account is linked, or the API refused it.</exception>
    /// <exception cref="HttpRequestException">The API couldn't be reached, or refused.</exception>
    public Task UnwatchAsync(ModRef modRef, CancellationToken cancellationToken)
        => SetTrackedAsync(NexusLink.ForMod(modRef), track: false, cancellationToken);

    private static ModRef ToModRef(NexusLink mod) => new(new GameRef(NexusId, mod.Game), mod.ModId);

    /// <summary>
    /// Whether the API refused this one request, rather than its rate limit (429) or itself (5xx). A refused account
    /// comes as <see cref="Abstractions.Accounts.AuthenticationFailedException"/> instead.
    /// </summary>
    private static bool RefusedTheRequest(HttpStatusCode? status)
        => status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError and not HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Sends the game in the query and the mod in a form, as the API takes them.
    /// </summary>
    /// <remarks>
    /// Nexus Mods doesn't document how it answers tracking a mod already tracked, or untracking one that isn't. So when
    /// it refuses, the tracked mods say whether the mod already is as asked, and only if it isn't is the refusal passed on.
    /// </remarks>
    private async Task SetTrackedAsync(NexusLink mod, bool track, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(track ? HttpMethod.Post : HttpMethod.Delete, new Uri($"{TrackedModsPath}?domain_name={mod.Game}", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("mod_id", mod.ModId)]),
        };

        try
        {
            await _api.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException refused) when (RefusedTheRequest(refused.StatusCode))
        {
            IReadOnlyList<ModRef> tracked = await GetWatchedAsync(cancellationToken).ConfigureAwait(false);
            if (tracked.Contains(ToModRef(mod)) != track)
            {
                throw;
            }
        }
    }
}
