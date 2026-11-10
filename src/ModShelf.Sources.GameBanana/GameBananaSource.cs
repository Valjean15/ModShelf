using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging;

using ModShelf.Domain.Mods;
using ModShelf.Sources.Abstractions.Common;
using ModShelf.Sources.GameBanana.Accounts;
using ModShelf.Sources.GameBanana.Api;
using ModShelf.Sources.GameBanana.Links;
using ModShelf.Sources.GameBanana.Resources;

namespace ModShelf.Sources.GameBanana;

/// <summary>
/// GameBanana (<c>gamebanana.com</c>), through <c>apiv11</c>, the JSON API its own pages use, which needs no account:
/// a member is linked by their public profile only, for what anyone can see of it.
/// Games and mods are named by GameBanana's IDs. Every request goes through the client the app sets up for the source.
/// </summary>
public sealed partial class GameBananaSource
{
    /// <summary>
    /// The source's ID. It's stored with every job, so it never changes.
    /// </summary>
    internal static readonly SourceId GameBananaId = new("gamebanana");

    private readonly GameBananaApiClient _api;
    private readonly ILogger<GameBananaSource> _logger;

    // Each game's top mod categories, by name, once read: a search by category names a category, and GameBanana an ID.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, long>> _categories = new(StringComparer.Ordinal);

    internal GameBananaSource(GameBananaApiClient api, ILogger<GameBananaSource> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _logger = logger;
        Strategies = [new GameBananaProfileStrategy(MemberIdAsync)];
    }

    public SourceId Id => GameBananaId;

    public string DisplayName => Strings.DisplayName;

    /// <summary>
    /// GameBanana counts likes, and its members subscribe to mods, which ModShelf reads but can't change.
    /// </summary>
    public SiteWords Words { get; } = new()
    {
        Followed = Strings.WordFollowed,
        Approval = Strings.WordApproval,
        Approvals = Strings.WordApprovals,
    };

    /// <summary>
    /// The game's ID on GameBanana.
    /// </summary>
    /// <exception cref="ArgumentException">The game isn't GameBanana's.</exception>
    private static string GameId(GameRef game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Source == GameBananaId && GameBananaLink.IsId(game.Key)
            ? game.Key
            : throw new ArgumentException($"The game '{game.Key}' of {game.Source} isn't GameBanana's.", nameof(game));
    }

    /// <summary>
    /// The mod's ID on GameBanana.
    /// </summary>
    /// <exception cref="ArgumentException">The mod isn't GameBanana's.</exception>
    private static string ModId(ModRef mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        GameId(mod.Game);
        return GameBananaLink.IsId(mod.Id)
            ? mod.Id
            : throw new ArgumentException($"The mod '{mod.Id}' isn't GameBanana's: its mods are numbers.", nameof(mod));
    }

    private static GameRef GameOf(long id) => new(GameBananaId, id.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The mod's profile: what it is, which game it's of, and its files.
    /// </summary>
    private Task<GameBananaMod> ProfileAsync(string modId, CancellationToken cancellationToken)
        => _api.GetAsync<GameBananaMod>($"apiv11/Mod/{modId}/ProfilePage", cancellationToken);
}
