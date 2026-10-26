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

    /// <param name="strategies">The ways to link an account, in the order to offer them.</param>
    internal NexusSource(NexusApiClient api, IReadOnlyList<IAuthStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(strategies);

        _api = api;
        Strategies = strategies;
    }

    public SourceId Id => NexusId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// An absolute HTTP or HTTPS link, or null for anything else, which the app must never open.
    /// </summary>
    private static Uri? WebLink(string? text)
        => Uri.TryCreate(text, UriKind.Absolute, out Uri? link) && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            ? link
            : null;
}
