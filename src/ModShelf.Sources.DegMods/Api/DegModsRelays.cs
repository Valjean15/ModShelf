using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// DEG Mods' relays, read together: each asked the same at once, and what they sent put together. Each holds part of
/// what DEG Mods has, so a relay that can't be reached leaves the rest; only when none can is it an error.
/// </summary>
internal sealed partial class DegModsRelays
{
    private readonly RelayClient _client;
    private readonly IReadOnlyList<Uri> _relays;
    private readonly ILogger<DegModsRelays> _logger;

    public DegModsRelays(RelayClient client, IReadOnlyList<Uri> relays, ILogger<DegModsRelays> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfZero(relays.Count);

        _client = client;
        _relays = relays;
        _logger = logger;
    }

    /// <returns>Every genuine event the relays sent for the filters, each once, in no particular order.</returns>
    /// <exception cref="HttpRequestException">No relay could be read; the first one's failure.</exception>
    public async Task<IReadOnlyList<NostrEvent>> ReadAsync(IReadOnlyList<NostrFilter> filters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);

        Task<IReadOnlyList<NostrEvent>>[] reads = [.. _relays.Select(relay => _client.ReadAsync(relay, filters, cancellationToken))];
        try
        {
            await Task.WhenAll(reads).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Each read is looked at below.
        }

        Dictionary<string, NostrEvent> found = new(StringComparer.Ordinal);
        HttpRequestException? first = null;
        int read = 0;
        for (int index = 0; index < reads.Length; index++)
        {
            Task<IReadOnlyList<NostrEvent>> task = reads[index];
            if (task.IsCompletedSuccessfully)
            {
                read++;
                foreach (NostrEvent nostrEvent in task.Result)
                {
                    found.TryAdd(nostrEvent.Id, nostrEvent);
                }
            }
            else if (task.Exception?.InnerException is HttpRequestException failed)
            {
                LogRelayFailed(_logger, failed, RelayClient.Name(_relays[index]));
                first ??= failed;
            }
            else
            {
                // Cancelled, or a failure that isn't the relay's: as it is.
                await task.ConfigureAwait(false);
            }
        }

        return read == 0 ? throw first! : [.. found.Values];
    }
}
