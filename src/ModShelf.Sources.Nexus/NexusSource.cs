using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Accounts;
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

    /// <summary>
    /// The website, where mods and files have their pages.
    /// </summary>
    private static readonly Uri Website = new("https://www.nexusmods.com/");

    private readonly NexusApiClient _api;
    private readonly TimeProvider _timeProvider;

    /// <param name="strategies">The ways to link an account, in the order to offer them.</param>
    internal NexusSource(NexusApiClient api, IReadOnlyList<IAuthStrategy> strategies, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _api = api;
        Strategies = strategies;
        _timeProvider = timeProvider;
    }

    public SourceId Id => NexusId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// A count the API gives, or null when it gives none or a negative one.
    /// </summary>
    private static long? Count(long? count) => count is >= 0 ? count : null;

    /// <summary>
    /// An absolute HTTP or HTTPS link, or null for anything else, which the app must never open.
    /// </summary>
    private static Uri? WebLink(string? text)
        => Uri.TryCreate(text, UriKind.Absolute, out Uri? link) && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            ? link
            : null;
}
