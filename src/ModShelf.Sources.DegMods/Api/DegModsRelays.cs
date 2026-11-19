using Microsoft.Extensions.Logging;

namespace ModShelf.Sources.DegMods.Api;

/// <summary>
/// DEG Mods' relays, read together: each asked the same at once, and what they sent put together. Each holds part of
/// what DEG Mods has, so a relay that can't be reached leaves the rest; only when none can is it an error. A relay that
/// doesn't answer in time rests for <see cref="RestFor"/>: it isn't asked meanwhile, so every read doesn't wait for it,
/// and it's logged once. When every relay rests, they're all asked.
/// </summary>
internal sealed partial class DegModsRelays
{
    /// <summary>
    /// How long a relay that didn't answer in time isn't asked.
    /// </summary>
    internal static readonly TimeSpan RestFor = TimeSpan.FromMinutes(5);

    private readonly RelayClient _client;
    private readonly IReadOnlyList<Uri> _relays;
    private readonly TimeProvider _time;
    private readonly ILogger<DegModsRelays> _logger;
    private readonly Lock _gate = new();

    // The relays resting, until when.
    private readonly Dictionary<Uri, DateTimeOffset> _resting = [];

    public DegModsRelays(RelayClient client, IReadOnlyList<Uri> relays, TimeProvider time, ILogger<DegModsRelays> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(relays);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfZero(relays.Count);

        _client = client;
        _relays = relays;
        _time = time;
        _logger = logger;
    }

    /// <returns>Every genuine event the relays asked sent for the filters, each once, in no particular order.</returns>
    /// <exception cref="HttpRequestException">No relay asked could be read; the first one's failure.</exception>
    public async Task<IReadOnlyList<NostrEvent>> ReadAsync(IReadOnlyList<NostrFilter> filters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filters);

        Uri[] asked = Awake();
        Task<IReadOnlyList<NostrEvent>>[] reads = [.. asked.Select(relay => _client.ReadAsync(relay, filters, cancellationToken))];
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
                Answered(asked[index]);
                foreach (NostrEvent nostrEvent in task.Result)
                {
                    found.TryAdd(nostrEvent.Id, nostrEvent);
                }
            }
            else if (task.Exception?.InnerException is HttpRequestException failed)
            {
                if (failed.InnerException is TimeoutException)
                {
                    Rest(asked[index], failed);
                }
                else
                {
                    LogRelayFailed(_logger, failed, RelayClient.Name(asked[index]));
                }

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

    /// <summary>
    /// The relays to ask: those not resting, or every one when they all are, since an answer late is better than none.
    /// </summary>
    private Uri[] Awake()
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_gate)
        {
            Uri[] awake = [.. _relays.Where(relay => !_resting.TryGetValue(relay, out DateTimeOffset until) || until <= now)];
            return awake.Length > 0 ? awake : [.. _relays];
        }
    }

    /// <summary>
    /// Leaves <paramref name="relay"/> out for <see cref="RestFor"/>, saying so unless it rests already, as when reads at
    /// once all waited for it.
    /// </summary>
    private void Rest(Uri relay, HttpRequestException late)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool resting;
        lock (_gate)
        {
            resting = _resting.TryGetValue(relay, out DateTimeOffset until) && until > now;
            _resting[relay] = now + RestFor;
        }

        if (!resting)
        {
            LogRelayRests(_logger, late, RelayClient.Name(relay), RestFor.TotalMinutes);
        }
    }

    /// <summary>
    /// <paramref name="relay"/> answered: it rests no more, saying so when it did.
    /// </summary>
    private void Answered(Uri relay)
    {
        bool rested;
        lock (_gate)
        {
            rested = _resting.Remove(relay);
        }

        if (rested)
        {
            string name = RelayClient.Name(relay);
            LogRelayAnswers(_logger, name);
        }
    }
}
