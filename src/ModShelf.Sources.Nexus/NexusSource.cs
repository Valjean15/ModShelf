using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.Nexus.Api;
using ModShelf.Sources.Nexus.Resources;

namespace ModShelf.Sources.Nexus;

/// <summary>
/// Nexus Mods (<c>nexusmods.com</c>), through its public API. Every request goes through the client the app sets
/// up for the source, which adds the linked account.
/// </summary>
public sealed partial class NexusSource : IAccountLinker
{
    /// <summary>
    /// The source's ID. It's stored with every job and linked account, so it never changes.
    /// </summary>
    internal static readonly SourceId NexusId = new("nexus");

    private readonly NexusApiClient _api;
    private readonly NexusQuota _quota;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NexusSource> _logger;

    // Each game's mod categories by ID, once read: they rarely change, and a mod names only its category's ID.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<long, string>> _categories = new(StringComparer.Ordinal);

    // Games' domain names by number, as each read of a game or of the list says: mods' requirements name games by number.
    private readonly ConcurrentDictionary<long, string> _gameDomains = new();

    // Whether the list of games was read while the app runs, so a requirement of a game not yet read can be named.
    private volatile bool _gamesListed;

    /// <param name="strategies">The ways to link an account, in the order to offer them.</param>
    /// <param name="quota">The account's rate limit, as the API client's last response reported it.</param>
    internal NexusSource(NexusApiClient api, IReadOnlyList<IAuthStrategy> strategies, NexusQuota quota, TimeProvider timeProvider, ILogger<NexusSource> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        Strategies = strategies;
        _quota = quota;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public SourceId Id => NexusId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// Nexus Mods tracks mods, and counts endorsements.
    /// </summary>
    public SiteWords Words { get; } = new()
    {
        Follow = Strings.WordFollow,
        Unfollow = Strings.WordUnfollow,
        Followed = Strings.WordFollowed,
        Approval = Strings.WordApproval,
        Approvals = Strings.WordApprovals,
    };

    /// <summary>
    /// A count the API gives, or null when it gives none or a negative one.
    /// </summary>
    private static long? Count(long? count) => count is >= 0 ? count : null;

    /// <summary>
    /// Whether a mod can be downloaded: not marked unavailable, and published, as far as the API says.
    /// </summary>
    private static bool IsAvailable(bool? available, string? status) => available != false && (status is null || status == "published");

    /// <summary>
    /// An absolute HTTP or HTTPS link, or null for anything else, which the app must never open.
    /// </summary>
    private static Uri? WebLink(string? text)
        => Uri.TryCreate(text, UriKind.Absolute, out Uri? link) && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            ? link
            : null;
}
