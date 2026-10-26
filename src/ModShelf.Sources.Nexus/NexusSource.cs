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
}
